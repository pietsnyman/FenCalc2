using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;
using FenCalc2.Models;
using FenCalc2.Services;

namespace FenCalc2.ViewModels;

/// <summary>
/// Envelope ▸ External Walls (SANS 10400-XA:2026 cl. 5.5). Multiple named wall
/// assemblies per building; every edit is write-through (no Apply button) and every
/// change re-runs the assessment + the wall's own report. Gated to 2026 buildings by
/// MainViewModel (the zone tables are energy-zone keyed).
/// </summary>
public partial class WallComplianceViewModel : ViewModelBase
{
    private readonly WallRepository _wallRepo;
    private readonly ProjectRepository _projectRepo;
    private readonly FenCalcProject _project;
    private readonly List<WallMaterial> _materials;
    private WallZoneRequirement? _req; // follows EnergyZone (walls are edition-independent)
    private bool _syncingMaterial;
    private WallCompliance.Assessment? _lastAssessment; // for the theme re-raise
    private WallAssembly? _lastWall;
    private IReadOnlyList<WallLayer>? _lastLayers;

    public sealed record CheckRow(string Name, string Verdict, string Detail);

    public ObservableCollection<WallRow> Walls { get; } = new();
    public ObservableCollection<WallLayerRow> Layers { get; } = new();
    public ObservableCollection<WallMaterial> Materials { get; }
    public ObservableCollection<string> SelectedPresets { get; } = new();
    public ObservableCollection<WallCheckRow> Checks { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    /// <summary>Energy zones for the walls page ("Zone 1"…"Zone 5H") — independent of
    /// the building's edition: tables 6/7 are keyed by the SITE's energy zone.</summary>
    public ObservableCollection<string> EnergyZones { get; } = new();

    public string[] AttachmentModes { get; } = { "None", "Included", "Excluded" };

    [ObservableProperty] private WallRow? _selectedWallRow;
    [ObservableProperty] private WallLayerRow? _selectedLayer;
    [ObservableProperty] private WallMaterial? _selectedMaterial;
    [ObservableProperty] private string? _energyZone;
    [ObservableProperty] private bool _capitalizeOutput;
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private string _classificationText = "—";
    [ObservableProperty] private string _totalRText = "—";
    [ObservableProperty] private string _cValueText = "—";
    [ObservableProperty] private string _CRText = "—";
    [ObservableProperty] private string _densityText = "—";
    [ObservableProperty] private string _requiredText = "—";
    [ObservableProperty] private string _resultText = "—";
    [ObservableProperty] private string _outputText = string.Empty;

    public int ProjectId => _project.Id;

    public WallComplianceViewModel(WallRepository wallRepo, ProjectRepository projectRepo, FenCalcProject project)
    {
        _wallRepo = wallRepo;
        _projectRepo = projectRepo;
        _project = project;
        _materials = wallRepo.GetMaterials().ToList();
        Materials = new ObservableCollection<WallMaterial>(_materials);
        foreach (var z in wallRepo.GetZones()) EnergyZones.Add(z);

        // Walls zone: the stored value wins; seed a 2026 building from its (energy)
        // climate zone once; a 2011 building starts empty until the user picks —
        // climatic zone numbers do NOT map onto energy zones (mix-and-match workflow).
        var zone = project.WallEnergyZone
                   ?? (project.StandardEdition == "2026" && EnergyZones.Contains(project.ClimateZone)
                       ? project.ClimateZone
                       : null);
        if (zone is not null && project.WallEnergyZone != zone)
        {
            project.WallEnergyZone = zone;
            projectRepo.Update(project);
        }
        _energyZone = zone;
        _req = ResolveReq(zone);

        foreach (var w in wallRepo.GetByProject(project.Id))
            Walls.Add(new WallRow(this, w));
        SelectedWallRow = Walls.FirstOrDefault();

        // Status priority: no walls > no zone > requirement row missing.
        if (Walls.Count == 0)
            StatusMessage = "No walls yet — press “New Wall” for a typical cavity build-up.";
        else if (zone is null)
            StatusMessage = "Pick the site’s energy zone — tables 6/7 are keyed by energy zone (annex C).";
        else if (_req is null)
            StatusMessage = $"No wall requirements seeded for {zone}.";
    }

    private WallZoneRequirement? ResolveReq(string? zoneDisplay) =>
        zoneDisplay is null
            ? null
            : _wallRepo.GetZoneRequirement(zoneDisplay.StartsWith("Zone ", StringComparison.Ordinal)
                ? zoneDisplay["Zone ".Length..]
                : zoneDisplay);

    partial void OnEnergyZoneChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        _project.WallEnergyZone = value;
        _projectRepo.Update(_project);
        _req = ResolveReq(value);
        if (_req is null)
            StatusMessage = $"No wall requirements seeded for {value}.";
        else if (StatusMessage.Contains("energy zone") || StatusMessage.Contains("requirements seeded"))
            StatusMessage = string.Empty;
        Recalculate();
    }

    // ---- selection --------------------------------------------------------------

    partial void OnSelectedWallRowChanged(WallRow? value)
    {
        Layers.Clear();
        if (value is not null)
            foreach (var l in _wallRepo.GetLayers(value.Data.Id))
                Layers.Add(new WallLayerRow(this, l));
        SelectedLayer = Layers.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelectedWall));
        Recalculate();
    }

    public bool HasSelectedWall => SelectedWallRow is not null;
    public bool HasSelectedLayer => SelectedLayer is not null;

    partial void OnSelectedLayerChanged(WallLayerRow? value)
    {
        _syncingMaterial = true;
        SelectedMaterial = value is null
            ? null
            : _materials.FirstOrDefault(m => m.Id == value.Data.MaterialId);
        _syncingMaterial = false;
        OnPropertyChanged(nameof(HasSelectedLayer));

        SelectedPresets.Clear();
        if (value is null) return;
        foreach (var p in (value.Data.Presets ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            SelectedPresets.Add(p);
    }

    partial void OnSelectedMaterialChanged(WallMaterial? value)
    {
        if (_syncingMaterial || SelectedLayer is null || value is null) return;
        ApplyMaterial(SelectedLayer, value);
    }

    partial void OnCapitalizeOutputChanged(bool value) => Recalculate();

    // ---- write-through persistence (called by the row classes) -------------------

    public void PersistWall(WallRow row)
    {
        _wallRepo.Update(row.Data);
        Recalculate();
    }

    public void OnLayerEdited(WallLayerRow row)
    {
        _wallRepo.UpdateLayer(row.Data);
        Recalculate();
    }

    private void ApplyMaterial(WallLayerRow row, WallMaterial mat)
    {
        if (row.Data.MaterialId == mat.Id) return;
        var d = row.Data;
        d.MaterialId = mat.Id;
        d.MaterialName = mat.Name;
        d.Category = mat.Category;
        d.FixedR = mat.FixedR;
        d.PlasterFree = mat.PlasterFree;
        d.Presets = mat.Presets;
        d.LibraryLambda = mat.Lambda;
        d.LibraryDensity = mat.Density;
        d.LibraryC = mat.CSpec;
        // A different material invalidates any manufacturer overrides (library wins again).
        d.LambdaOverride = null;
        d.DensityOverride = null;
        d.COverride = null;
        if (mat.Category == "Film") d.ThicknessMm = 0;
        _wallRepo.UpdateLayer(d);
        row.NotifyMaterialChanged();
        OnSelectedLayerChanged(row); // refresh preset chips + combo sync
        Recalculate();
    }

    // ---- walls -------------------------------------------------------------------

    private string NextWallName()
    {
        for (var n = 1; ; n++)
        {
            var name = $"Wall {n}";
            if (Walls.All(w => w.Name != name)) return name;
        }
    }

    [RelayCommand]
    private void NewWall()
    {
        var wall = new WallAssembly { ProjectId = _project.Id, Name = NextWallName() };
        wall.Id = _wallRepo.Insert(wall);

        // Starter build-up = the typical SA cavity wall (user decision: nominal leaves
        // with composite λ, standard set thicknesses — everything editable afterwards).
        var starter = new (string Mat, double Mm)[]
        {
            ("Outside air film (7 m/s)", 0),
            ("Cement plaster / render", 15),
            ("Clay brick masonry (incl. joints)", 110),
            ("Air cavity (unventilated, non-reflective)", 50),
            ("Clay brick masonry (incl. joints)", 110),
            ("Cement plaster / render", 15),
            ("Inside air film (still air)", 0),
        };
        var pos = 0;
        foreach (var (matName, mm) in starter)
        {
            var mat = _materials.FirstOrDefault(x => x.Name == matName);
            if (mat is null) continue;
            var d = NewLayerData(wall.Id, mat, mm);
            d.Pos = pos++;
            d.Id = _wallRepo.InsertLayer(d);
        }

        var row = new WallRow(this, wall);
        Walls.Add(row);
        SelectedWallRow = row; // triggers the layer load + first assessment
        StatusMessage = $"Added {wall.Name} with a typical cavity build-up — edit freely.";
    }

    [RelayCommand]
    private void DeleteWall()
    {
        if (SelectedWallRow is null) return;
        var name = SelectedWallRow.Name;
        _wallRepo.Delete(SelectedWallRow.Data.Id); // cascades the layers
        Walls.Remove(SelectedWallRow);
        SelectedWallRow = Walls.FirstOrDefault();
        StatusMessage = $"Deleted {name}.";
    }

    // ---- layers ------------------------------------------------------------------

    private WallLayer NewLayerData(int wallId, WallMaterial mat, double mm) => new()
    {
        WallId = wallId,
        MaterialId = mat.Id,
        Name = mat.Name,
        Category = mat.Category,
        ThicknessMm = mat.Category == "Film" ? 0 : mm,
        FixedR = mat.FixedR,
        PlasterFree = mat.PlasterFree,
        Presets = mat.Presets,
        LibraryLambda = mat.Lambda,
        LibraryDensity = mat.Density,
        LibraryC = mat.CSpec,
    };

    private static double MiddlePreset(WallMaterial mat)
    {
        var parts = (mat.Presets ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return 0;
        return Helpers.ParseTolerant(parts[(parts.Length - 1) / 2]) ?? 0;
    }

    /// <summary>Persist the current row order (drag/drop and ▲▼ funnel through here).</summary>
    private void Renumber()
    {
        _wallRepo.ReplaceOrder(Layers.Select(r => r.Data.Id));
        for (var i = 0; i < Layers.Count; i++)
            Layers[i].Data.Pos = i;
    }

    [RelayCommand]
    private void AddLayer()
    {
        if (SelectedWallRow is null)
        {
            StatusMessage = "Select or add a wall first.";
            return;
        }
        var mat = (SelectedLayer?.Data.MaterialId is int id
                    ? _materials.FirstOrDefault(m => m.Id == id)
                    : null)
                  ?? _materials.FirstOrDefault(m => m.Name == "Cement plaster / render")
                  ?? _materials.FirstOrDefault();
        if (mat is null) return;

        var d = NewLayerData(SelectedWallRow.Data.Id, mat, MiddlePreset(mat));
        d.Id = _wallRepo.InsertLayer(d);
        var insertIndex = SelectedLayer is null ? Layers.Count : Layers.IndexOf(SelectedLayer) + 1;
        var row = new WallLayerRow(this, d);
        Layers.Insert(insertIndex, row);
        Renumber();
        SelectedLayer = row;
        StatusMessage = "Layer added.";
    }

    [RelayCommand]
    private void RemoveLayer()
    {
        if (SelectedLayer is null) return;
        var idx = Layers.IndexOf(SelectedLayer);
        _wallRepo.DeleteLayer(SelectedLayer.Data.Id);
        Layers.Remove(SelectedLayer);
        Renumber();
        SelectedLayer = Layers.Count == 0 ? null : Layers[Math.Min(idx, Layers.Count - 1)];
        StatusMessage = "Layer removed.";
    }

    /// <summary>Drag/drop and the ▲▼ buttons both land here.</summary>
    public void MoveLayer(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Layers.Count || to >= Layers.Count)
            return;
        var item = Layers[from];
        Layers.Move(from, to);
        Renumber();
        if (!ReferenceEquals(SelectedLayer, item)) SelectedLayer = item;
        Recalculate();
    }

    /// <summary>Row-level ▲▼ buttons — the view passes the clicked row (binding paths
    /// into a DataTemplate can't reach the window's commands with compiled bindings).</summary>
    public void MoveRow(WallLayerRow row, int delta) =>
        MoveLayer(Layers.IndexOf(row), Layers.IndexOf(row) + delta);

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedLayer is null) return;
        MoveLayer(Layers.IndexOf(SelectedLayer), Layers.IndexOf(SelectedLayer) - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedLayer is null) return;
        MoveLayer(Layers.IndexOf(SelectedLayer), Layers.IndexOf(SelectedLayer) + 1);
    }

    [RelayCommand]
    private void SetThickness(string? preset)
    {
        if (SelectedLayer is null || string.IsNullOrWhiteSpace(preset)) return;
        SelectedLayer.SetThicknessFromPreset(preset); // persists + re-raises ThicknessText
        OnPropertyChanged(nameof(SelectedLayer));     // belt & braces: force the path re-read
    }

    // ---- assessment --------------------------------------------------------------

    private void Recalculate()
    {
        if (SelectedWallRow is null)
        {
            ClassificationText = TotalRText = CValueText = CRText = DensityText =
                RequiredText = ResultText = "—";
            Checks.Clear();
            Warnings.Clear();
            HasWarnings = false;
            OutputText = string.Empty;
            _lastWall = null;
            _lastLayers = null;
            _lastAssessment = null;
            return;
        }

        var wall = SelectedWallRow.Data;
        var layers = Layers.Select(r => r.Data).ToList();
        var a = WallCompliance.Assess(layers, wall, _req);
        _lastWall = wall;
        _lastLayers = layers;
        _lastAssessment = a;

        ClassificationText = a.IsHeavy ? "Heavy wall" : "Light wall";
        DensityText = a.SurfaceDensity.ToString("0.0");
        TotalRText = a.TotalR.ToString("0.000");
        CValueText = a.CArea.ToString("0");
        CRText = a.CR.ToString("0.0");
        ResultText = a.Result;
        RequiredText = _req is null
            ? (EnergyZone is null ? "no energy zone selected" : "not seeded")
            : a.IsHeavy
                ? $"{_req.HeavyMinR:0.00} m²K/W (table 6)" +
                  (wall.Category1 ? " — not applied, category 1" : $" · {_req.HeavyConstruction}")
                : $"R {_req.LightMinR:0.00} m²K/W or CR {_req.LightMinCR:0} h (table 7)";

        Checks.Clear();
        foreach (var c in a.Checks) Checks.Add(new WallCheckRow(c.Name, c.Verdict, c.Detail));
        Warnings.Clear();
        foreach (var w in a.Warnings) Warnings.Add(w);
        HasWarnings = a.Warnings.Count > 0;

        OutputText = WallCompliance.BuildReport(wall, layers, _req, a, CapitalizeOutput);
    }

    /// <summary>
    /// Re-raises the verdict texts/rows after a runtime theme switch so the
    /// converter-resolved verdict brushes re-run — the wall page is a TAB now (no
    /// longer modal), so the theme menu IS reachable while it is displayed.
    /// Called from MainViewModel.RefreshThemedBrushes.
    /// </summary>
    public void RefreshThemedBrushes()
    {
        OnPropertyChanged(nameof(ResultText));
        if (_lastAssessment is null || _lastWall is null || _lastLayers is null) return;
        var rows = _lastAssessment.Checks
            .Select(c => new WallCheckRow(c.Name, c.Verdict, c.Detail)).ToList();
        Checks.Clear();
        foreach (var r in rows) Checks.Add(r);
    }
}
