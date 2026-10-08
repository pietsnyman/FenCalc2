using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;
using FenCalc2.Models;
using FenCalc2.Services;

namespace FenCalc2.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ProjectRepository _projectRepo;
    private readonly FloorRepository _floorRepo;
    private readonly WindowPlacementRepository _windowRepo;
    private readonly CatalogueRepository _catalogueRepo;
    private readonly WallRepository _wallRepo;
    private readonly SolarCalculator _solarCalc;
    private readonly OutputFormatter _outputFormatter;

    // In-memory state
    private FenCalcProject? _currentProject;
    private Floor? _currentFloor;
    private List<WindowPlacement> _allWindows = new();
    private List<Floor> _allFloors = new();
    // XA:2026 reference tables (seeded by Xa2026Seeder before the UI exists) — lazily
    // loaded once per session; only 2026-edition projects ever query them.
    private List<FenestrationBand>? _bands2026;
    private List<ShadingMultiplier>? _multipliers2026;
    private int _rotationOffset;
    private bool _syncingTopDirection; // SelectedOrientation display-sync, not a user pick

    // Selected-window editor state (staged edits; DB writes only on Apply Changes)
    private bool _refreshingEditor;
    private bool _editorStatusIsWarning;
    private bool _updatingTypeDimensions; // programmatic W/H update (type/range change)
    private bool _sizeDirty;              // user typed W/H this session (not yet applied)
    private List<WindowType> _typeCache = new();
    private List<GlazingPerformance> _glazingCache = new();
    private List<string> _frameCache = new();

    public MainViewModel()
    {
        _projectRepo = new ProjectRepository(Database.CreateConnection);
        _floorRepo = new FloorRepository(Database.CreateConnection);
        _windowRepo = new WindowPlacementRepository(Database.CreateConnection);
        _catalogueRepo = new CatalogueRepository(Database.CreateConnection);
        _wallRepo = new WallRepository(Database.CreateConnection);
        _solarCalc = new SolarCalculator(_catalogueRepo);
        _outputFormatter = new OutputFormatter(_solarCalc);

        // Restore the output display toggles from the GLOBAL AppSettings store (same
        // mechanism as Theme/LastClient — universal, not per-project). Assign the
        // FIELDS so no change-events fire at startup: bindings read the values when
        // they attach, and the first LoadProject renders with them. Missing keys = false.
        _useThreeDecimals = _projectRepo.GetSetting("UseThreeDecimals") == "true";
        _capitalizeOutput = _projectRepo.GetSetting("CapitalizeOutput") == "true";
        _combineFloors = _projectRepo.GetSetting("CombineFloors") == "true";

        // Restore the last main tab (field assign → no change-event at startup);
        // sanitized by OnSelectedMainTabChanged / OnHasProjectChanged when needed.
        if (int.TryParse(_projectRepo.GetSetting("MainTab"), out var mainTab) && mainTab is 0 or 1)
            _selectedMainTab = mainTab;

        // Zone combo starts on the legacy list; LoadProject refills it per edition.
        RefreshClimateZoneList("2011");

        // Annex C town names for the site autocomplete (one read, ~400 rows).
        foreach (var t in _catalogueRepo.GetTowns()) TownNames.Add(t.Town);
    }

    // Orientation constants
    public static string[] OrientationNames { get; } =
        { "North", "North East", "East", "South East", "South", "South West", "West", "North West" };

    private static readonly int[] GridToLogical = { 7, 0, 1, 6, -1, 2, 5, 4, 3 };
    private static readonly int[] PlanSlotToGrid = BuildPlanSlotToGrid();

    private static int[] BuildPlanSlotToGrid()
    {
        var map = new int[8];
        for (int grid = 0; grid < 9; grid++)
        {
            var slot = GridToLogical[grid];
            if (slot >= 0)
                map[slot] = grid;
        }
        return map;
    }

    // Project Info
    [ObservableProperty] private string _clientName = string.Empty;
    [ObservableProperty] private string _projectName = string.Empty;
    [ObservableProperty] private string _buildingName = string.Empty;
    [ObservableProperty] private string _selectedClimateZone = "Zone 1";
    // Which SANS 10400-XA edition this building is assessed against ("2011" | "2026");
    // the left-panel combo edits it, RecalculateOutput dispatches on the project value.
    [ObservableProperty] private string _selectedStandardEdition = "2011";
    // Visibility driver for the 2026-only site fields (left panel + editor shading row)
    [ObservableProperty] private bool _isStandard2026;
    // Main tab host: 0 = Fenestration, 1 = External Walls (persisted as AppSettings
    // "MainTab"). The walls tab is available on EVERY edition — walls carry their own
    // energy zone (Projects.WallEnergyZone), so they never depend on StandardEdition.
    [ObservableProperty] private int _selectedMainTab;
    [ObservableProperty] private WallComplianceViewModel? _wallsVm;
    // Left-panel site (XA:2026): town from annex C, latitude (drives table 3 M), SCCP flag
    [ObservableProperty] private string _townName = string.Empty;
    [ObservableProperty] private string _siteLatitude = string.Empty;
    [ObservableProperty] private bool _siteSccp;
    private bool _loadingSite; // LoadProject pushes stored values — no lookups, no DB writes
    [ObservableProperty] private string _selectedOrientation = "North";
    [ObservableProperty] private string _selectedFloorName = "Ground Floor";
    [ObservableProperty] private string _floorArea = string.Empty;
    [ObservableProperty] private bool _hasProject;

    // Compass window lists
    [ObservableProperty] private ObservableCollection<WindowCard> _northWindows = new();
    [ObservableProperty] private ObservableCollection<WindowCard> _northEastWindows = new();
    [ObservableProperty] private ObservableCollection<WindowCard> _eastWindows = new();
    [ObservableProperty] private ObservableCollection<WindowCard> _southEastWindows = new();
    [ObservableProperty] private ObservableCollection<WindowCard> _southWindows = new();
    [ObservableProperty] private ObservableCollection<WindowCard> _southWestWindows = new();
    [ObservableProperty] private ObservableCollection<WindowCard> _westWindows = new();
    [ObservableProperty] private ObservableCollection<WindowCard> _northWestWindows = new();

    // Compass labels
    [ObservableProperty] private string _labelN = "N";
    [ObservableProperty] private string _labelNE = "NE";
    [ObservableProperty] private string _labelE = "E";
    [ObservableProperty] private string _labelSE = "SE";
    [ObservableProperty] private string _labelS = "S";
    [ObservableProperty] private string _labelSW = "SW";
    [ObservableProperty] private string _labelW = "W";
    [ObservableProperty] private string _labelNW = "NW";

    // Floor plan image
    [ObservableProperty] private Bitmap? _floorPlanImage;
    [ObservableProperty] private bool _isMirrored;

    // Selected Window
    [ObservableProperty] private WindowCard? _selectedWindowCard;
    [ObservableProperty] private int _selectedWindowId = -1;
    [ObservableProperty] private string _selWinType = string.Empty;
    [ObservableProperty] private string _selOrientation = string.Empty;
    [ObservableProperty] private string _selGlass = string.Empty;
    [ObservableProperty] private string _selFrame = string.Empty;
    [ObservableProperty] private string _selUValue = string.Empty;
    [ObservableProperty] private string _selSHGC = string.Empty;
    [ObservableProperty] private string _selWidth = string.Empty;
    [ObservableProperty] private string _selHeight = string.Empty;
    [ObservableProperty] private string _selP = string.Empty;
    [ObservableProperty] private string _selG = string.Empty;
    // XA:2026 shading classification of the selected window (editor row, 2026 projects)
    [ObservableProperty] private string _selShading = "Auto";
    [ObservableProperty] private string _selArea = string.Empty;
    [ObservableProperty] private bool _hasSelectedWindow;
    [ObservableProperty] private string _selRange = string.Empty;
    [ObservableProperty] private string _editorStatus = string.Empty;
    [ObservableProperty] private IBrush _editorStatusForeground = StatusMutedBrush;

    // Output
    [ObservableProperty] private string _outputText = string.Empty;
    [ObservableProperty] private bool _useThreeDecimals;
    [ObservableProperty] private bool _capitalizeOutput;
    [ObservableProperty] private bool _combineFloors;

    // Combo sources
    public string[] Orientations { get; } = OrientationNames;
    // Zone list is EDITION-dependent and rebuilt by RefreshClimateZoneList: 2011 = the
    // six legacy climatic zones, 2026 = the annex C energy zones (1-7 + 5H, seeded by
    // Xa2026Seeder). ObservableCollection so refills notify the ComboBox; the NAME stays
    // "ClimateZones" because MainWindow.axaml binds it.
    public ObservableCollection<string> ClimateZones { get; } = new();
    public static string[] LegacyClimateZones { get; } =
        { "Zone 1", "Zone 2", "Zone 3", "Zone 4", "Zone 5", "Zone 6" };
    public string[] StandardEditions { get; } = { "2011", "2026" };
    public string[] SelShadingOptions { get; } = { "Auto", "Shaded", "Unshaded" };
    public ObservableCollection<string> TownNames { get; } = new();
    // Default floor-name sequence; beyond Sixth Floor new floors become "Floor 7", "Floor 8"…
    public static string[] DefaultFloorNames { get; } = { "Ground Floor", "First Floor", "Second Floor", "Third Floor", "Fourth Floor", "Fifth Floor", "Sixth Floor" };

    // The OPEN building's actual floors (RefreshFloorNames) — never a fixed list: a fixed
    // list let users pick floors the building doesn't have, which nulled _currentFloor
    // and silently killed Add Window / Copy / image pick.
    public ObservableCollection<string> FloorNames { get; } = new();
    [ObservableProperty] private string _floorCountText = string.Empty;

    // Selected-window editor floor (staged; Apply moves the window)
    public ObservableCollection<string> SelFloorNames { get; } = new();
    [ObservableProperty] private string _selFloor = string.Empty;

    // Selected-window editor combo sources (refilled per selection)
    public string[] SelOrientations { get; } = OrientationNames;
    public ObservableCollection<string> SelRangeNames { get; } = new();
    public ObservableCollection<string> SelWinTypeNames { get; } = new();
    public ObservableCollection<string> SelGlassNames { get; } = new();
    public ObservableCollection<string> SelFrameNames { get; } = new();

    // --- Compass Logic ---

    // Labels are TRUE compass directions of each plan slot: slot's true direction comes
    // from the project's north anchor, shifted by the user's rotation offset.
    private string GetOrientationForGridPosition(int gridIndex)
    {
        var slot = GridToLogical[gridIndex];
        if (slot < 0) return string.Empty;
        var anchorIdx = NorthAnchorIndex;
        var label = (slot + anchorIdx + _rotationOffset) % 8;
        return OrientationNames[label];
    }

    private int NorthAnchorIndex
    {
        get
        {
            var idx = Array.IndexOf(OrientationNames, _currentProject?.Orientation ?? "North");
            return idx < 0 ? 0 : idx;
        }
    }

    private ObservableCollection<WindowCard> GetCollectionForGridPosition(int gridIndex)
    {
        return gridIndex switch
        {
            0 => NorthWestWindows,
            1 => NorthWindows,
            2 => NorthEastWindows,
            3 => WestWindows,
            5 => EastWindows,
            6 => SouthWestWindows,
            7 => SouthWindows,
            8 => SouthEastWindows,
            _ => new ObservableCollection<WindowCard>()
        };
    }

    private void RefreshCompass()
    {
        // The left Orientation dropdown reads the direction at the TOP of the plan
        // (anchor + rotation offset) — keep it in step with every ring change.
        _syncingTopDirection = true;
        SelectedOrientation = OrientationNames[(NorthAnchorIndex + _rotationOffset) % 8];
        _syncingTopDirection = false;

        LabelNW = GetOrientationForGridPosition(0);
        LabelN = GetOrientationForGridPosition(1);
        LabelNE = GetOrientationForGridPosition(2);
        LabelW = GetOrientationForGridPosition(3);
        LabelE = GetOrientationForGridPosition(5);
        LabelSW = GetOrientationForGridPosition(6);
        LabelS = GetOrientationForGridPosition(7);
        LabelSE = GetOrientationForGridPosition(8);

        NorthWestWindows.Clear();
        NorthWindows.Clear();
        NorthEastWindows.Clear();
        WestWindows.Clear();
        EastWindows.Clear();
        SouthWestWindows.Clear();
        SouthWindows.Clear();
        SouthEastWindows.Clear();

        if (_currentFloor is null) return;

        var floorWindows = _allWindows.Where(w => w.FloorId == _currentFloor.Id).ToList();

        // Windows are pinned to their plan slot (image-relative): the label ring may
        // rotate, but each window always sits in the slot where it was added.
        foreach (var wp in floorWindows)
        {
            var slotIdx = Array.IndexOf(OrientationNames, wp.Orientation);
            if (slotIdx < 0) continue;

            var gridIdx = PlanSlotToGrid[slotIdx];
            var collection = GetCollectionForGridPosition(gridIdx);

            collection.Add(new WindowCard
            {
                PlacementId = wp.Id,
                DisplayName = wp.WinType,
                Orientation = wp.Orientation
            });
        }
    }

    private void RefreshSelectedWindowEditor()
    {
        _refreshingEditor = true;
        _sizeDirty = false;
        try
        {
            ReloadEditorCatalogue();
            RefillRangeNames();
            SelFloorNames.Clear();
            foreach (var n in StoreyOrderedFloorNames()) SelFloorNames.Add(n);
            SelGlassNames.Clear();
            foreach (var g in _glazingCache.Select(g => g.Glass)) SelGlassNames.Add(g);
            SelFrameNames.Clear();
            foreach (var f in _frameCache) SelFrameNames.Add(f);

            if (SelectedWindowId < 0)
            {
                HasSelectedWindow = false;
                SelWinType = string.Empty;
                SelRange = string.Empty;
                SelFloor = string.Empty;
                SelOrientation = string.Empty;
                SelGlass = string.Empty;
                SelFrame = string.Empty;
                SelUValue = string.Empty;
                SelSHGC = string.Empty;
                SelWidth = string.Empty;
                SelHeight = string.Empty;
                SelP = string.Empty;
                SelG = string.Empty;
                SelShading = "Auto";
                SelArea = string.Empty;
                SelWinTypeNames.Clear();
                ClearEditorStatus();
                return;
            }

            var wp = _allWindows.FirstOrDefault(w => w.Id == SelectedWindowId);
            if (wp is null) return;

            HasSelectedWindow = true;

            // Derived range of the stored type; inject missing values so combos still
            // display legacy names that are no longer in the catalogue.
            var range = _typeCache.FirstOrDefault(t => t.Name == wp.WinType)?.WinRange ?? string.Empty;
            if (!string.IsNullOrEmpty(range) && !SelRangeNames.Contains(range)) SelRangeNames.Add(range);
            RefillWinTypes(range);
            if (!string.IsNullOrEmpty(wp.WinType) && !SelWinTypeNames.Contains(wp.WinType)) SelWinTypeNames.Add(wp.WinType);
            if (!string.IsNullOrEmpty(wp.WinGlass) && !SelGlassNames.Contains(wp.WinGlass)) SelGlassNames.Add(wp.WinGlass);
            if (!string.IsNullOrEmpty(wp.WinFrame) && !SelFrameNames.Contains(wp.WinFrame)) SelFrameNames.Add(wp.WinFrame);

            SelRange = range;
            // Staged floor — Apply Changes moves the window there (the compass follows).
            var floorName = _allFloors.FirstOrDefault(f => f.Id == wp.FloorId)?.FloorName ?? string.Empty;
            if (floorName.Length > 0 && !SelFloorNames.Contains(floorName)) SelFloorNames.Add(floorName);
            SelFloor = floorName;
            SelWinType = wp.WinType;
            // Show the TRUE label the user sees; ApplyChanges converts back via SlotForLabel.
            SelOrientation = LabelForSlot(wp.Orientation);
            SelGlass = wp.WinGlass;
            SelFrame = wp.WinFrame;
            SelUValue = wp.UValue.ToString("F2");
            SelSHGC = wp.SHGC.ToString("F2");
            SelWidth = wp.WinWidth.ToString("F3");
            SelHeight = wp.WinHeight.ToString("F3");
            SelP = wp.P.ToString();
            SelG = wp.G.ToString();
            SelShading = wp.ShadingOverride switch { 1 => "Shaded", 0 => "Unshaded", _ => "Auto" };
            SelArea = (wp.WinWidth * wp.WinHeight).ToString("F3");
        }
        finally
        {
            _refreshingEditor = false;
        }
        ClearEditorStatus();
    }

    // --- Selected-window editor staging helpers ---

    // Reloaded per selection so Catalogue-menu edits show up immediately.
    private void ReloadEditorCatalogue()
    {
        _typeCache = _catalogueRepo.GetAllTypes().ToList();
        _glazingCache = _catalogueRepo.GetAllGlazing().ToList();
        _frameCache = _catalogueRepo.GetAllFrames().Select(f => f.FrameType).ToList();
    }

    private void RefillRangeNames()
    {
        SelRangeNames.Clear();
        foreach (var r in _typeCache.Select(t => t.WinRange).Where(r => !string.IsNullOrEmpty(r)).Distinct())
            SelRangeNames.Add(r);
    }

    private void RefillWinTypes(string range)
    {
        SelWinTypeNames.Clear();
        foreach (var t in _typeCache.Where(t => t.WinRange == range).Select(t => t.Name).Distinct())
            SelWinTypeNames.Add(t);
    }

    // Preferred match inside the staged range, else any range (legacy names may repeat).
    private WindowType? FindType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!string.IsNullOrEmpty(SelRange))
        {
            var inRange = _typeCache.FirstOrDefault(t => t.WinRange == SelRange && t.Name == name);
            if (inRange is not null) return inRange;
        }
        return _typeCache.FirstOrDefault(t => t.Name == name);
    }

    // Glass/Frame changed: manufacturer override on the type else GlazingPerformance default.
    private void UpdateEnergyValues()
    {
        var (u, shgc) = Helpers.ComputeUShgc(FindType(SelWinType), SelGlass, SelFrame, _glazingCache);
        SelUValue = u;
        SelSHGC = shgc;
    }

    // Range/Type changed: dimensions follow the selected type, everything else stays
    // as the user left it (P/G/Glass/Frame/U/SHGC only change when edited explicitly).
    private void ApplyTypeDimensions()
    {
        // Programmatic W/H update: must not raise the mismatch warning or count as a
        // manual resize — and it discards any hand-edited (not yet applied) size.
        _updatingTypeDimensions = true;
        try
        {
            var type = FindType(SelWinType);
            if (type is not null)
            {
                if (type.WinWidth.HasValue) SelWidth = type.WinWidth.Value.ToString("F3");
                if (type.WinHeight.HasValue) SelHeight = type.WinHeight.Value.ToString("F3");
            }
        }
        finally
        {
            _updatingTypeDimensions = false;
        }
        _sizeDirty = false;
        UpdateSizeStatus(); // clears a stale manual-edit warning: values now match the type
        UpdateArea();
    }

    private void UpdateArea()
    {
        var w = Helpers.ParseTolerant(SelWidth);
        var h = Helpers.ParseTolerant(SelHeight);
        if (w is not null && h is not null)
            SelArea = (w.Value * h.Value).ToString("F3");
    }

    // Status colors, aligned with the App.axaml Warning/Success tokens (resolved per
    // active theme variant so they re-tint with View > Theme)
    private static IBrush ResolveStatusBrush(string key, string fallbackHex)
    {
        if (Application.Current is { } app
            && app.TryFindResource(key, app.ActualThemeVariant, out var value)
            && value is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    private static readonly IBrush StatusMutedBrush = new SolidColorBrush(Color.Parse("#6B7280"));

    private void SetEditorStatus(string message, bool warning)
    {
        _editorStatusIsWarning = warning;
        EditorStatus = message;
        EditorStatusForeground = warning
            ? ResolveStatusBrush("WarningBrush", "#B45309")
            : ResolveStatusBrush("SuccessBrush", "#15803D");
    }

    private void ClearEditorStatus() => SetEditorStatus(string.Empty, false);

    // Re-raise bindings whose values are resolved at convert/assignment time so a
    // runtime theme switch re-tints them (compass tiles, editor status line).
    public void RefreshThemedBrushes()
    {
        OnPropertyChanged(nameof(LabelNW));
        OnPropertyChanged(nameof(LabelN));
        OnPropertyChanged(nameof(LabelNE));
        OnPropertyChanged(nameof(LabelW));
        OnPropertyChanged(nameof(LabelE));
        OnPropertyChanged(nameof(LabelSW));
        OnPropertyChanged(nameof(LabelS));
        OnPropertyChanged(nameof(LabelSE));

        if (EditorStatus.Length > 0)
        {
            EditorStatusForeground = ResolveStatusBrush(
                _editorStatusIsWarning ? "WarningBrush" : "SuccessBrush",
                _editorStatusIsWarning ? "#B45309" : "#15803D");
        }

        // The walls page is a tab (not a modal), so its converter-resolved verdict
        // brushes also need a re-raise when the theme changes.
        WallsVm?.RefreshThemedBrushes();
    }

    // Hand-edited W/H vs the DB row: warn only while the user's manual edit is
    // unapplied. Type/range-driven updates are not dirty — they clear a stale warning
    // instead (the values match the newly selected type, so there is nothing to fix).
    private void UpdateSizeStatus()
    {
        if (_refreshingEditor || SelectedWindowId < 0) return;

        if (!_sizeDirty)
        {
            if (_editorStatusIsWarning) ClearEditorStatus();
            return;
        }

        var wp = _allWindows.FirstOrDefault(w => w.Id == SelectedWindowId);
        if (wp is null) return;

        var w = Helpers.ParseTolerant(SelWidth);
        var h = Helpers.ParseTolerant(SelHeight);
        if (w is null || h is null) return; // mid-edit; blur/Apply report invalid input

        if (Math.Abs(w.Value - wp.WinWidth) > 1e-9 || Math.Abs(h.Value - wp.WinHeight) > 1e-9)
            SetEditorStatus($"W and H no longer match the database window ({wp.WinWidth:F3} x {wp.WinHeight:F3}) — Apply Changes will save them (as a new Custom type).", warning: true);
        else if (_editorStatusIsWarning)
            ClearEditorStatus();
    }

    partial void OnSelRangeChanged(string value)
    {
        if (_refreshingEditor) return;
        RefillWinTypes(value);
        SelWinType = SelWinTypeNames.FirstOrDefault() ?? string.Empty;
        ApplyTypeDimensions();
    }

    partial void OnSelWinTypeChanged(string value)
    {
        if (_refreshingEditor) return;
        ApplyTypeDimensions();
    }

    partial void OnSelGlassChanged(string value)
    {
        if (_refreshingEditor) return;
        UpdateEnergyValues();
    }

    partial void OnSelFrameChanged(string value)
    {
        if (_refreshingEditor) return;
        UpdateEnergyValues();
    }

    partial void OnSelWidthChanged(string value)
    {
        if (_updatingTypeDimensions) return; // programmatic; ApplyTypeDimensions post-evaluates
        _sizeDirty = true;
        UpdateSizeStatus();
    }

    partial void OnSelHeightChanged(string value)
    {
        if (_updatingTypeDimensions) return;
        _sizeDirty = true;
        UpdateSizeStatus();
    }

    // W/H LostFocus: refresh the area label when both values are valid.
    public void OnEditorSizeChanged()
    {
        if (SelectedWindowId < 0) return;
        var w = Helpers.ParseTolerant(SelWidth);
        var h = Helpers.ParseTolerant(SelHeight);
        if (w is null || h is null)
        {
            SetEditorStatus("Width and height must be valid numbers.", warning: true);
            return;
        }
        SelArea = (w.Value * h.Value).ToString("F3");
        UpdateSizeStatus();
    }

    // --- Commands ---

    [RelayCommand]
    private void SelectWindow(WindowCard? card)
    {
        if (card is null)
        {
            SelectedWindowId = -1;
            SelectedWindowCard = null;
        }
        else
        {
            SelectedWindowId = card.PlacementId;
            SelectedWindowCard = card;
        }
        RefreshSelectedWindowEditor();
    }

    [RelayCommand]
    private void RotateClockwise()
    {
        _rotationOffset = (_rotationOffset + 1) % 8;
        PersistCompassState();
        RefreshCompass();
        RecalculateOutput();
    }

    [RelayCommand]
    private void RotateAnticlockwise()
    {
        _rotationOffset = (_rotationOffset + 7) % 8;
        PersistCompassState();
        RefreshCompass();
        RecalculateOutput();
    }

    [RelayCommand]
    private void MirrorBuilding()
    {
        if (_currentProject is null) return;

        // Mirror bakes the left/right swap into the stored plan slots and flips the
        // image flag; reopening shows the mirrored state.
        _windowRepo.MirrorOrientations(_currentProject.Id);
        foreach (var wp in _allWindows)
            wp.Orientation = MirroredSlot(wp.Orientation);

        _currentProject.IsMirrored = !_currentProject.IsMirrored;
        IsMirrored = _currentProject.IsMirrored;
        PersistCompassState();
        RefreshCompass();
        RecalculateOutput();
    }

    // Flips ONLY the displayed plan image (shared IsMirrored display flag, persisted like
    // Mirror Building's) — window data stays exactly as it is. Needed because Mirror
    // Building couples the slot swap with the image flip: without this, a building in the
    // mirrored state force-flips every image that gets loaded and there is no way back
    // without also swapping the window directions.
    [RelayCommand]
    private void FlipImage()
    {
        if (_currentProject is null) return;

        _currentProject.IsMirrored = !_currentProject.IsMirrored;
        IsMirrored = _currentProject.IsMirrored;
        PersistCompassState();
    }

    private void PersistCompassState()
    {
        if (_currentProject is null) return;
        _currentProject.RotationOffset = _rotationOffset;
        _projectRepo.UpdateCompassState(_currentProject.Id, _rotationOffset, _currentProject.IsMirrored);
    }

    // True compass label displayed on a given plan slot (ring rotation included)
    public string LabelForSlot(string slot)
    {
        var slotIdx = Array.IndexOf(OrientationNames, slot);
        if (slotIdx < 0) return slot;
        return OrientationNames[(slotIdx + NorthAnchorIndex + _rotationOffset) % 8];
    }

    // Inverse: the plan slot of the box that currently shows a given TRUE label —
    // used when adding/copying/moving a window "to the box labeled X".
    private string SlotForLabel(string label)
    {
        var labelIdx = Array.IndexOf(OrientationNames, label);
        if (labelIdx < 0) return label;
        return OrientationNames[(labelIdx - (NorthAnchorIndex + _rotationOffset) + 16) % 8];
    }

    // Mirror swaps the three left plan slots with the three right ones (N/S stay)
    public static string MirroredSlot(string slot) => slot switch
    {
        "North East" => "North West",
        "North West" => "North East",
        "East" => "West",
        "West" => "East",
        "South East" => "South West",
        "South West" => "South East",
        _ => slot
    };

    // The range most represented by the project's current windows; fresh projects
    // default to Aluminium Top Hung.
    private string DefaultRangeForDialog()
    {
        var typeRange = new Dictionary<string, string>();
        foreach (var t in _catalogueRepo.GetAllTypes())
            typeRange[t.Name] = t.WinRange;

        var best = _allWindows
            .Where(w => !string.IsNullOrEmpty(w.WinType) && typeRange.ContainsKey(w.WinType))
            .GroupBy(w => typeRange[w.WinType])
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        return best ?? "Aluminium Top Hung";
    }

    // Add Window P/G pre-population: the building's most frequent (P, G) PAIR among its
    // windows — ties go to the newest window (highest Id). Null = the building has no
    // windows yet, so the dialog keeps its 375/160 defaults. Both Add and Apply Changes
    // feed this automatically: they are what writes those rows.
    public static (int p, int g)? MostCommonPG(IEnumerable<WindowPlacement> windows)
    {
        var best = windows
            .GroupBy(w => (w.P, w.G))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(w => w.Id))
            .FirstOrDefault();
        return best is null ? null : (best.Key.P, best.Key.G);
    }

    [RelayCommand]
    private void AddWindow(string orientation)
    {
        if (_currentProject is null || _currentFloor is null) return;

        var vm = new AddWindowViewModel(_catalogueRepo, orientation,
            StoreyOrderedFloorNames(), _currentFloor?.FloorName, DefaultRangeForDialog());
        // Pre-fill P/G with the building's most-used pair (newest wins a tie).
        var pg = MostCommonPG(_allWindows);
        if (pg is not null)
        {
            vm.P = pg.Value.p.ToString();
            vm.G = pg.Value.g.ToString();
        }
        var dialog = new Views.AddWindowView
        {
            DataContext = vm
        };

        // Dialog is shown from code-behind; result checked after close
        ShowAddWindowDialog?.Invoke(dialog, vm);
    }

    // Event for code-behind to show the dialog
    public event Action<Views.AddWindowView, AddWindowViewModel>? ShowAddWindowDialog;

    public void OnAddWindowDialogClosed(AddWindowViewModel vm)
    {
        if (!vm.DialogResult || _currentProject is null || _currentFloor is null) return;

        // The dialog's floor dropdown may target a different floor than the one shown;
        // unknown/blank name falls back to the current floor.
        var targetFloor = _allFloors.FirstOrDefault(f => f.FloorName == vm.SelectedFloor) ?? _currentFloor;

        var placement = vm.ToWindowPlacement(_currentProject.Id, targetFloor.Id);
        if (placement is null) return;

        // Dialog orientation is the TRUE-facing label the user sees (e.g. "West");
        // store the plan slot of the box that currently shows that label.
        placement.Orientation = SlotForLabel(placement.Orientation);

        var id = _windowRepo.Insert(placement);
        placement.Id = id;
        _allWindows.Add(placement);

        // Follow the new window: switch the compass to the floor it landed on
        // (also refreshes the floor area/image, compass cards and output) while the
        // editor's selection survives the card rebuild.
        PreserveSelection(() => ShowFloor(targetFloor.FloorName));
    }

    [RelayCommand]
    private void ApplyChanges()
    {
        if (SelectedWindowId < 0) return;

        var wp = _allWindows.FirstOrDefault(w => w.Id == SelectedWindowId);
        if (wp is null) return;

        // Parse staged numerics (tolerant to comma/dot decimals); nothing is written
        // to the DB until every field checks out.
        var w = Helpers.ParseTolerant(SelWidth);
        if (w is null) { SetEditorStatus("Invalid width — nothing applied.", warning: true); return; }
        var h = Helpers.ParseTolerant(SelHeight);
        if (h is null) { SetEditorStatus("Invalid height — nothing applied.", warning: true); return; }
        var p = Helpers.ParseTolerant(SelP);
        if (p is null) { SetEditorStatus("Invalid P — nothing applied.", warning: true); return; }
        var g = Helpers.ParseTolerant(SelG);
        if (g is null) { SetEditorStatus("Invalid G — nothing applied.", warning: true); return; }
        var u = Helpers.ParseTolerant(SelUValue);
        if (u is null) { SetEditorStatus("Invalid U-value — nothing applied.", warning: true); return; }
        var shgc = Helpers.ParseTolerant(SelSHGC);
        if (shgc is null) { SetEditorStatus("Invalid SHGC — nothing applied.", warning: true); return; }

        // Resolve the (possibly custom-typed) window type. Existing WindowTypes rows
        // are NEVER updated here — editing them belongs to the future Catalogue >
        // Edit Window in Range. A hand-resized W/H snapshots a new entry under Custom.
        var typed = SelWinType.Trim();
        if (typed.Length == 0) { SetEditorStatus("Type name is empty — nothing applied.", warning: true); return; }

        ReloadEditorCatalogue();
        string typeStatus;
        var existing = _typeCache.FirstOrDefault(t => t.Name == typed);
        var dimsDiffer = existing is null
            || existing.WinWidth is null || existing.WinHeight is null
            || Math.Abs(existing.WinWidth.Value - w.Value) > 1e-9
            || Math.Abs(existing.WinHeight.Value - h.Value) > 1e-9;
        var manualResize = existing is not null && _sizeDirty && dimsDiffer;

        if (existing is not null && !manualResize)
        {
            typeStatus = $"Using existing type '{existing.Name}' (range {existing.WinRange}).";
            wp.WinType = existing.Name;
        }
        else
        {
            _catalogueRepo.InsertRange("Custom");
            // Global name uniqueness keeps placement→type (name-only) resolution sane.
            var name = typed;
            for (int n = 1; _typeCache.Any(t => t.Name == name); n++)
                name = $"{typed}-{n}";
            _catalogueRepo.InsertType(new WindowType
            {
                WinRange = "Custom",
                Name = name,
                WinWidth = w,
                WinHeight = h,
                WinGlass = string.IsNullOrEmpty(SelGlass) ? null : SelGlass,
                WinFrame = string.IsNullOrEmpty(SelFrame) ? null : SelFrame,
                UValue = u,
                SHGC = shgc
            });
            wp.WinType = name;
            if (existing is null)
                typeStatus = name == typed
                    ? $"Saved new type '{typed}' under range Custom."
                    : $"Type name taken — created and used '{name}' under range Custom.";
            else
                typeStatus = $"Saved W/H change as '{name}' under range Custom — original '{typed}' unchanged.";
        }

        // Orientation is the TRUE label the user sees; store the plan slot of the box
        // that currently shows it (same conversion as Add/Copy).
        if (OrientationNames.Contains(SelOrientation))
            wp.Orientation = SlotForLabel(SelOrientation);
        if (!string.IsNullOrEmpty(SelGlass)) wp.WinGlass = SelGlass;
        if (!string.IsNullOrEmpty(SelFrame)) wp.WinFrame = SelFrame;
        wp.WinWidth = w.Value;
        wp.WinHeight = h.Value;
        wp.P = (int)Math.Round(p.Value);
        wp.G = (int)Math.Round(g.Value);
        wp.UValue = u.Value;
        wp.SHGC = shgc.Value;
        // XA:2026 shading classification (5.2.2): null = automatic P ≥ H × M test.
        wp.ShadingOverride = SelShading switch { "Shaded" => 1, "Unshaded" => 0, _ => null };

        // Staged floor: resolve BEFORE the write (Update persists FloorId too), then
        // move the view to the destination so the moved window stays visible.
        var targetFloor = _allFloors.FirstOrDefault(f => f.FloorName == SelFloor);
        var moved = targetFloor is not null && targetFloor.Id != wp.FloorId;
        if (targetFloor is not null) wp.FloorId = targetFloor.Id;

        _windowRepo.Update(wp);
        // Compass cards are rebuilt (new objects) — keep the editor's target across it.
        PreserveSelection(() =>
        {
            RefreshCompass();
            if (moved) ShowFloor(targetFloor!.FloorName);
        });
        RefreshSelectedWindowEditor(); // clears status and picks up the new Custom type
        RecalculateOutput();
        SetEditorStatus(moved ? $"{typeStatus} Moved to {targetFloor!.FloorName}." : typeStatus,
            warning: false);
    }

    // RefreshCompass rebuilds every card list (new WindowCard objects), which drops the
    // ListBox selection — the TwoWay SelectedItem binding pushes null into
    // SelectedWindowCard and SelectionChanged clears SelectedWindowId. Re-select the
    // same window afterwards so the Selected Window editor keeps its target.
    private void PreserveSelection(Action refresh)
    {
        var keep = SelectedWindowId;
        refresh();
        if (keep < 0 || SelectedWindowId == keep) return;

        var card = AllCards().FirstOrDefault(c => c.PlacementId == keep);
        if (card is null) return;

        SelectedWindowCard = card;
        SelectedWindowId = keep;
        RefreshSelectedWindowEditor();
    }

    private IEnumerable<WindowCard> AllCards() =>
        NorthWindows.Concat(NorthEastWindows).Concat(EastWindows).Concat(SouthEastWindows)
            .Concat(SouthWindows).Concat(SouthWestWindows).Concat(WestWindows).Concat(NorthWestWindows);

    [RelayCommand]
    private void RemoveWindow()
    {
        if (SelectedWindowId < 0) return;

        _windowRepo.Delete(SelectedWindowId);
        _allWindows.RemoveAll(w => w.Id == SelectedWindowId);
        SelectedWindowId = -1;
        SelectedWindowCard = null;
        RefreshSelectedWindowEditor();
        RefreshCompass();
        RecalculateOutput();
    }

    [RelayCommand]
    private void CopyWindowTo()
    {
        if (SelectedWindowId < 0 || _currentProject is null || _allFloors.Count == 0) return;

        var wp = _allWindows.FirstOrDefault(w => w.Id == SelectedWindowId);
        if (wp is null) return;

        // Prechecked: the window's own floor and its current TRUE direction label.
        var vm = new CopyWindowViewModel(
            StoreyOrderedFloorNames(),
            _allFloors.FirstOrDefault(f => f.Id == wp.FloorId)?.FloorName,
            LabelForSlot(wp.Orientation));
        ShowCopyWindowDialog?.Invoke(vm);
    }

    public event Action<CopyWindowViewModel>? ShowCopyWindowDialog;

    public void OnCopyWindowDialogClosed(CopyWindowViewModel vm)
    {
        if (!vm.DialogResult || _currentProject is null) return;

        var wp = _allWindows.FirstOrDefault(w => w.Id == SelectedWindowId);
        if (wp is null) return;

        var count = 0;
        foreach (var floorName in vm.SelectedFloors)
        {
            var floor = _allFloors.FirstOrDefault(f => f.FloorName == floorName);
            if (floor is null) continue;

            foreach (var dir in vm.SelectedDirections)
            {
                // Directions are TRUE labels; store the plan slot of the box that
                // currently shows each one (same conversion as Add/Move).
                var clone = Helpers.CloneWindow(wp, _currentProject.Id, floor.Id, SlotForLabel(dir));
                clone.Id = _windowRepo.Insert(clone);
                _allWindows.Add(clone);
                count++;
            }
        }

        PreserveSelection(RefreshCompass);
        RecalculateOutput();
        ImportResultMessage = count == 1
            ? "Copied 1 window."
            : $"Copied {count} windows.";
    }

    private void RecalculateOutput()
    {
        if (_currentProject is null || _allFloors.Count == 0)
        {
            OutputText = string.Empty;
            return;
        }

        if (_currentProject.StandardEdition == "2026")
        {
            _bands2026 ??= _catalogueRepo.GetFenestrationBands().ToList();
            _multipliers2026 ??= _catalogueRepo.GetShadingMultipliers().ToList();
            // Per storey ALWAYS: cl. 5.3.5 Note 2 forbids trading storeys off, so the
            // Combine-floors toggle intentionally does not reach this formatter.
            OutputText = Fenestration2026.Format(
                _currentProject,
                _allFloors,
                _allWindows,
                _bands2026,
                _multipliers2026,
                UseThreeDecimals,
                CapitalizeOutput);
            return;
        }

        OutputText = _outputFormatter.Format(
            _currentProject,
            _allFloors,
            _allWindows,
            UseThreeDecimals,
            CapitalizeOutput,
            CombineFloors);
    }

    partial void OnUseThreeDecimalsChanged(bool value)
    {
        RecalculateOutput();
        _projectRepo.SetSetting("UseThreeDecimals", value ? "true" : "false");
    }

    partial void OnCapitalizeOutputChanged(bool value)
    {
        RecalculateOutput();
        _projectRepo.SetSetting("CapitalizeOutput", value ? "true" : "false");
    }

    // Global calculation mode (same store as the other output toggles): off = each
    // floor calculated separately (default), on = all floors together as one.
    partial void OnCombineFloorsChanged(bool value)
    {
        RecalculateOutput();
        _projectRepo.SetSetting("CombineFloors", value ? "true" : "false");
    }

    partial void OnSelectedFloorNameChanged(string value)
    {
        if (_currentProject is null) return;
        // Unknown/empty name (e.g. the combo briefly pushes null while its list is being
        // refilled) keeps the current floor — _currentFloor must never go null while the
        // building has floors, or Add Window / Copy / image pick stop working.
        var floor = string.IsNullOrEmpty(value) ? null : _allFloors.FirstOrDefault(f => f.FloorName == value);
        if (floor is null) return;
        _currentFloor = floor;
        if (_currentFloor is not null)
            FloorArea = _currentFloor.FloorArea?.ToString("0.00") ?? string.Empty;
        LoadFloorImage();
        RefreshCompass();
        RecalculateOutput();
    }

    partial void OnSelectedClimateZoneChanged(string value)
    {
        // null/empty = the ComboBox pushed SelectedItem mid-refill (same trap as the
        // floor combo, see OnSelectedFloorNameChanged) — keep the current zone; never
        // persist null into a NOT NULL column.
        if (_currentProject is null || string.IsNullOrEmpty(value)) return;
        _currentProject.ClimateZone = value;
        _projectRepo.Update(_currentProject);
        RecalculateOutput();
    }

    // Left-panel standard selector. Zone MEANING differs between the schemes (e.g.
    // Pretoria = Zone 2 on the 2011 climatic map, Zone 5 on the 2026 energy map), so
    // switching refills the zone list and only keeps a zone that exists there.
    partial void OnSelectedStandardEditionChanged(string value)
    {
        if (_currentProject is null) return;
        IsStandard2026 = value == "2026";
        _currentProject.StandardEdition = value;
        _projectRepo.Update(_currentProject);
        _projectRepo.Update(_currentProject);
        RefreshClimateZoneList(value);
        RecalculateOutput();
        ImportResultMessage = value == "2026"
            ? "Standard: SANS 10400-XA:2026 — re-check the energy zone and set the site latitude."
            : "Standard: SANS 10400-XA:2011 (legacy report).";
    }

    // ---- Left-panel site fields (XA:2026) ------------------------------------------
    // Selecting/typing a town that exists in annex C fills zone + latitude + SCCP.
    // The standard's list is NOT exhaustive (Sutherland, Kroonstad, Benoni … are simply
    // absent), so any other name is kept as free text — never an error.
    partial void OnTownNameChanged(string value)
    {
        if (_loadingSite || _currentProject is null) return;
        _currentProject.Town = value;
        var match = string.IsNullOrWhiteSpace(value) ? null : _catalogueRepo.GetTown(value.Trim());
        if (match is not null)
        {
            var zone = "Zone " + match.EnergyZone;
            if (ClimateZones.Contains(zone)) SelectedClimateZone = zone; // persists via its handler
            SiteLatitude = match.Latitude.ToString("0.000");             // persists via its handler
            SiteSccp = match.Sccp != 0;
        }
        _projectRepo.Update(_currentProject); // Town itself (zone/lat/sccp go through their handlers)
        RecalculateOutput();
    }

    partial void OnSiteLatitudeChanged(string value)
    {
        if (_loadingSite || _currentProject is null) return;
        _currentProject.Latitude = Helpers.ParseTolerant(value); // null = cleared = no shading credit
        _projectRepo.Update(_currentProject);
        RecalculateOutput();
    }

    partial void OnSiteSccpChanged(bool value)
    {
        if (_loadingSite || _currentProject is null) return;
        _currentProject.Sccp = value;
        _projectRepo.Update(_currentProject);
        RecalculateOutput();
    }

    // Rebuild the zone combo for the edition. The current selection survives when it
    // still exists in the new list; otherwise the first zone is selected — and because
    // Clear() can push null through the binding first, the value is always reassigned.
    private void RefreshClimateZoneList(string edition)
    {
        var zones = (edition == "2026"
                ? _catalogueRepo.GetEnergyZoneList().Select(z => "Zone " + z)
                : LegacyClimateZones)
            .ToList();
        if (zones.Count == 0) zones = LegacyClimateZones.ToList(); // seeder-failure guard

        var keep = SelectedClimateZone;
        ClimateZones.Clear();
        foreach (var z in zones) ClimateZones.Add(z);
        SelectedClimateZone = zones.Contains(keep) ? keep : zones[0];
    }

    partial void OnSelectedOrientationChanged(string value)
    {
        if (_syncingTopDirection) return; // display sync from RefreshCompass, not a pick
        if (_currentProject is null) return;

        // The dropdown is the ABSOLUTE top direction: set the anchor to the chosen
        // value and collapse the rotation offset — only anchor+offset is ever
        // observable, so labels are identical and re-picking the shown value is a no-op.
        _currentProject.Orientation = value;
        _rotationOffset = 0;
        _currentProject.RotationOffset = 0; // Update() persists this from the object
        _projectRepo.Update(_currentProject);
        RefreshCompass();
        RecalculateOutput(); // TrueOrientation changed — was missing here before
    }

    // --- Floors (dynamic list, add/duplicate/show) ---

    // Refill the combo sources from the building's REAL floors in storey order and
    // update the "N floors" indicator (empty when no project is open).
    private void RefreshFloorNames()
    {
        var ordered = StoreyOrderedFloorNames();

        FloorNames.Clear();
        foreach (var n in ordered) FloorNames.Add(n);

        FloorCountText = _currentProject is null || ordered.Count == 0
            ? string.Empty
            : ordered.Count == 1 ? "1 floor" : $"{ordered.Count} floors";

        // The ComboBox's Clear() pushes null/"" into SelectedFloorName mid-refill —
        // re-assert the current floor AFTER refilling so the dropdown shows the
        // selected floor (project open, add/rename/delete all land through here).
        // Unknown/null values are ignored by the handler, so only a real drift re-fires.
        if (_currentFloor is not null && SelectedFloorName != _currentFloor.FloorName)
            SelectedFloorName = _currentFloor.FloorName;
    }

    // The floor directly below `name` in storey order (null when it is the lowest
    // floor). New floors inherit their plan image from it — add and duplicate.
    private Floor? FloorBelowName(string name)
    {
        var ordered = _allFloors
            .OrderBy(f => OutputFormatter.FloorOrder(f.FloorName))
            .ThenBy(f => f.Id)
            .ToList();
        var idx = ordered.FindIndex(f => f.FloorName == name);
        return idx > 0 ? ordered[idx - 1] : null;
    }

    // Storey-ordered floor names of the open building (defaults 0–6, then "Floor N",
    // anything else last). Shared by the floor combo and the dialog pickers.
    private List<string> StoreyOrderedFloorNames() => _allFloors
        .OrderBy(f => OutputFormatter.FloorOrder(f.FloorName))
        .ThenBy(f => f.Id)
        .Select(f => f.FloorName)
        .ToList();

    // First unused default name (Ground…Sixth), else the smallest free "Floor N" (N ≥ 7).
    public static string NextFreeFloorName(IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var n in DefaultFloorNames)
            if (!used.Contains(n)) return n;
        for (int i = DefaultFloorNames.Length; ; i++)
        {
            var candidate = $"Floor {i}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    // Display a floor on the compass. Setting SelectedFloorName runs the full refresh via
    // its handler; if the name is already selected, refresh explicitly (no change event).
    private void ShowFloor(string floorName)
    {
        if (SelectedFloorName != floorName)
        {
            SelectedFloorName = floorName;
            return;
        }

        _currentFloor = _allFloors.FirstOrDefault(f => f.FloorName == floorName) ?? _currentFloor;
        FloorArea = _currentFloor?.FloorArea?.ToString("0.00") ?? string.Empty;
        LoadFloorImage();
        RefreshCompass();
        RecalculateOutput();
    }

    // After dialogs that inserted floors/windows through their own repos.
    private void ReloadFloorsAndShow(string floorName)
    {
        if (_currentProject is null) return;
        _allFloors = _floorRepo.GetByProject(_currentProject.Id).ToList();
        _allWindows = _windowRepo.GetByProject(_currentProject.Id).ToList();
        RefreshFloorNames();
        ShowFloor(floorName);
    }

    [RelayCommand]
    private void AddFloor()
    {
        if (_currentProject is null)
        {
            ImportResultMessage = "Open a project first.";
            return;
        }

        var name = NextFreeFloorName(_allFloors.Select(f => f.FloorName));
        var floor = new Floor { ProjectId = _currentProject.Id, FloorName = name };
        floor.Id = _floorRepo.Insert(floor);
        _allFloors.Add(floor);

        // Area + plan image are inherited from the floor directly BELOW the new one
        // (e.g. with only a Ground Floor present, First Floor starts with Ground's
        // picture and area) — the user can click the image box / edit the area to
        // give the floor its own values.
        var below = FloorBelowName(name);
        floor.FloorArea = below?.FloorArea;
        floor.ImagePath = Helpers.CopyFloorImage(below?.ImagePath, _currentProject.Id, floor.Id);
        _floorRepo.Update(floor);

        RefreshFloorNames();
        ShowFloor(name);
        ImportResultMessage = below is not null && below.FloorArea is not null
            ? $"Added {name} — floor area copied from {below.FloorName}; update it if it differs."
            : $"Added {name}.";
    }

    public void OnFloorAreaChanged()
    {
        if (_currentFloor is null) return;
        var area = Helpers.ParseTolerant(FloorArea);
        if (area is not null)
        {
            _currentFloor.FloorArea = area.Value;
            _floorRepo.UpdateArea(_currentFloor.Id, area.Value);
            RecalculateOutput();
        }
    }

    public void LoadProject(FenCalcProject project)
    {
        _currentProject = project;
        ClientName = project.ClientName;
        ProjectName = project.ProjectName;
        BuildingName = project.BuildingName;
        // Refill the zone combo for the project's standard BEFORE restoring its zone:
        // the 2011 climatic and 2026 energy zone lists are different lists entirely.
        SelectedStandardEdition = project.StandardEdition;
        if (ClimateZones.Contains(project.ClimateZone))
            SelectedClimateZone = project.ClimateZone;
        else
            SelectedClimateZone = ClimateZones[0]; // clamps a zone from the other scheme

        // Site fields (2026): guard so the town lookup never re-runs mid-load and
        // overwrites values that were edited after the town was originally picked.
        _loadingSite = true;
        TownName = project.Town ?? string.Empty;
        SiteLatitude = project.Latitude?.ToString("0.000") ?? string.Empty;
        SiteSccp = project.Sccp;
        _loadingSite = false;
        // SelectedOrientation is NOT set here: the rotation offset isn't restored yet,
        // and this assignment would fire the user-pick handler against the previous
        // project's top. RefreshCompass() below syncs the derived top direction.
        HasProject = true;

        _allFloors = _floorRepo.GetByProject(project.Id).ToList();
        _allWindows = _windowRepo.GetByProject(project.Id).ToList();

        // Resolve the floor BEFORE refilling the combo list (a refill can push null
        // into SelectedFloorName through the binding).
        var preferredFloor = SelectedFloorName;
        _currentFloor = _allFloors.FirstOrDefault(f => f.FloorName == preferredFloor)
                       ?? _allFloors.FirstOrDefault();
        RefreshFloorNames();

        if (_currentFloor is not null)
        {
            SelectedFloorName = _currentFloor.FloorName;
            FloorArea = _currentFloor.FloorArea?.ToString("0.00") ?? string.Empty;
        }

        // Restore persisted compass state so reopening looks the same as when it closed
        _rotationOffset = project.RotationOffset;
        IsMirrored = project.IsMirrored;
        _currentProject.IsMirrored = project.IsMirrored;
        LoadFloorImage();
        RefreshCompass();
        RecalculateOutput();

        _projectRepo.SetSetting("LastClient", project.ClientName);
        _projectRepo.SetSetting("LastProject", project.ProjectName);
        _projectRepo.SetSetting("LastBuilding", project.BuildingName);

        // The walls page follows the open building — rebuilt every load so it never
        // holds a stale project reference (same-edition A→B switches fire no events).
        WallsVm = new WallComplianceViewModel(_wallRepo, _projectRepo, project);
    }

    // Dialog events
    public event Action<NewProjectViewModel>? ShowNewProjectDialog;
    public event Action<OpenProjectViewModel>? ShowOpenProjectDialog;
    public event Action<AddRangeViewModel>? ShowAddRangeDialog;
    public event Action<DeleteRangeViewModel>? ShowDeleteRangeDialog;
    public event Action<AddWindowTypeViewModel>? ShowAddWindowTypeDialog;
    public event Action<DeleteWindowTypeViewModel>? ShowDeleteWindowTypeDialog;
    public event Action<NewFromExistingViewModel>? ShowNewFromExistingDialog;
    public event Action<DeleteProjectViewModel>? ShowDeleteProjectDialog;
    public event Action<DuplicateFloorViewModel>? ShowDuplicateFloorDialog;
    public event Action<RenameFloorViewModel>? ShowRenameFloorDialog;
    public event Action<DeleteFloorViewModel>? ShowDeleteFloorDialog;
    public event Action<AboutViewModel>? ShowAboutDialog;
    public event Action<GettingStartedViewModel>? ShowGettingStartedDialog;

    // Help > About: static info dialog (logo, version, MIT licence) — nothing to hand
    // back on close, so like AddRange/DeleteRange it needs no OnXxxDialogClosed.
    [RelayCommand]
    private void About() => ShowAboutDialog?.Invoke(new AboutViewModel());

    // Help > Getting Started: the user guide (all text lives in the view).
    [RelayCommand]
    private void GettingStarted() => ShowGettingStartedDialog?.Invoke(new GettingStartedViewModel());

    // Main tab (0 = Fenestration, 1 = External Walls): persisted per session choice;
    // the walls tab needs its VM, so a click without a project snaps back to tab 0.
    partial void OnSelectedMainTabChanged(int value)
    {
        if (value == 1 && WallsVm is null)
        {
            _selectedMainTab = 0;
            OnPropertyChanged(nameof(SelectedMainTab));
            return;
        }
        _projectRepo.SetSetting("MainTab", value.ToString());
    }

    partial void OnHasProjectChanged(bool value)
    {
        if (value) return;
        WallsVm = null;
        if (SelectedMainTab == 1) SelectedMainTab = 0;
    }

    [RelayCommand]
    private void NewProject()
    {
        var vm = new NewProjectViewModel(_projectRepo, _floorRepo, _catalogueRepo);
        ShowNewProjectDialog?.Invoke(vm);
    }

    public void OnNewProjectDialogClosed(NewProjectViewModel vm)
    {
        if (!vm.DialogResult || vm.CreatedProject is null) return;
        LoadProject(vm.CreatedProject);
    }

    [RelayCommand]
    private void OpenProject()
    {
        var vm = new OpenProjectViewModel(_projectRepo,
            _projectRepo.GetSetting("LastClient"),
            _projectRepo.GetSetting("LastProject"),
            _projectRepo.GetSetting("LastBuilding"));
        ShowOpenProjectDialog?.Invoke(vm);
    }

    public void OnOpenProjectDialogClosed(OpenProjectViewModel vm)
    {
        if (!vm.DialogResult || vm.SelectedProject_ is null) return;
        LoadProject(vm.SelectedProject_);
    }

    [RelayCommand]
    private void NewFromExisting()
    {
        var vm = new NewFromExistingViewModel(_projectRepo, _floorRepo, _windowRepo,
            _projectRepo.GetSetting("LastClient"),
            _projectRepo.GetSetting("LastProject"),
            _projectRepo.GetSetting("LastBuilding"));
        ShowNewFromExistingDialog?.Invoke(vm);
    }

    public void OnNewFromExistingDialogClosed(NewFromExistingViewModel vm)
    {
        // Open the freshly duplicated building for editing (LoadProject also
        // refreshes the Last* keys to the clone).
        if (!vm.DialogResult || vm.CreatedProject is null) return;
        LoadProject(vm.CreatedProject);
    }

    [RelayCommand]
    private void DeleteProject()
    {
        // No prefill — the user picks the building with the dialog's own cascade.
        ShowDeleteProjectDialog?.Invoke(new DeleteProjectViewModel(_projectRepo));
    }

    public void OnDeleteProjectDialogClosed(DeleteProjectViewModel vm)
    {
        if (!vm.DialogResult || vm.SelectedProject_ is null) return;
        var p = vm.SelectedProject_;

        // If this is the building being edited, drop to the empty state BEFORE the
        // row disappears (property partials null-guard, nothing persists mid-close).
        if (_currentProject?.Id == p.Id)
            CloseCurrentProject();

        // Floor-plan images are project-owned copies — remove them with the building.
        foreach (var f in _floorRepo.GetByProject(p.Id))
        {
            try
            {
                if (!string.IsNullOrEmpty(f.ImagePath) && File.Exists(f.ImagePath))
                    File.Delete(f.ImagePath);
            }
            catch
            {
                // Locked/already-missing file — a harmless leftover beats a failed delete.
            }
        }

        // Cascades Floors + WindowPlacements (Foreign Keys=True in Database.cs).
        _projectRepo.Delete(p.Id);

        // Never leave Last* pointing at a ghost building (open or not).
        if (_projectRepo.GetSetting("LastClient") == p.ClientName
            && _projectRepo.GetSetting("LastProject") == p.ProjectName
            && _projectRepo.GetSetting("LastBuilding") == p.BuildingName)
        {
            _projectRepo.SetSetting("LastClient", "");
            _projectRepo.SetSetting("LastProject", "");
            _projectRepo.SetSetting("LastBuilding", "");
        }

        ImportResultMessage = $"Deleted {p.ClientName} / {p.ProjectName} / {p.BuildingName}.";
    }

    // Project > Duplicate Floor: source floor + new unique name (prefilled next free).
    [RelayCommand]
    private void DuplicateFloor()
    {
        if (_currentProject is null || _allFloors.Count == 0)
        {
            ImportResultMessage = "Open a project first.";
            return;
        }

        var vm = new DuplicateFloorViewModel(
            StoreyOrderedFloorNames(),
            _currentFloor?.FloorName,
            NextFreeFloorName(_allFloors.Select(f => f.FloorName)));
        ShowDuplicateFloorDialog?.Invoke(vm);
    }

    public void OnDuplicateFloorDialogClosed(DuplicateFloorViewModel vm)
    {
        if (!vm.DialogResult || vm.CreatedFloorName is null || _currentProject is null) return;

        var source = _allFloors.FirstOrDefault(f => f.FloorName == vm.SelectedSourceFloor);
        if (source is null) return;

        // Every window of the source floor, field-for-field (FloorId remapped per copy).
        var sourceWindows = _allWindows.Where(w => w.FloorId == source.Id).ToList();

        // Copy 1 takes the typed name; copies 2..N get the next free floor names,
        // generated over a set that grows with each created floor.
        var usedNames = _allFloors.Select(f => f.FloorName).ToList();
        var createdNames = new List<string>();
        var areaInherited = false;

        for (int i = 0; i < vm.CopyCount; i++)
        {
            var name = i == 0 ? vm.CreatedFloorName : NextFreeFloorName(usedNames);

            // Floor row first; area + plan image are inherited from the floor directly
            // BELOW this new one (same rule as Add Floor — NOT the source floor;
            // consecutive copies chain: copy 2 inherits from copy 1). The row joins
            // _allFloors right away so the next iteration can chain off it; a final
            // ReloadFloorsAndShow re-reads everything from the DB anyway.
            var floor = new Floor
            {
                ProjectId = _currentProject.Id,
                FloorName = name
            };
            floor.Id = _floorRepo.Insert(floor);
            _allFloors.Add(floor);

            var below = FloorBelowName(name);
            floor.FloorArea = below?.FloorArea;
            floor.ImagePath = Helpers.CopyFloorImage(below?.ImagePath, _currentProject.Id, floor.Id);
            _floorRepo.Update(floor);
            if (below is not null && below.FloorArea is not null)
                areaInherited = true;

            foreach (var w in sourceWindows)
            {
                var clone = Helpers.CloneWindow(w, _currentProject.Id, floor.Id);
                clone.Id = _windowRepo.Insert(clone);
                _allWindows.Add(clone);
            }

            usedNames.Add(name);
            createdNames.Add(name);
        }

        // Re-read both lists from the DB (they were written through the repos), then
        // refill the floor combo and switch the compass to the LAST new floor.
        ReloadFloorsAndShow(createdNames[^1]);
        var areaNote = areaInherited
            ? " Floor area copied from the floor below — update it if it differs."
            : string.Empty;
        ImportResultMessage = vm.CopyCount == 1
            ? $"Duplicated {source.FloorName} as {createdNames[0]} " +
              $"({sourceWindows.Count} window(s) copied)." + areaNote
            : $"Duplicated {source.FloorName} into {vm.CopyCount} floors " +
              $"({sourceWindows.Count} window(s) each)." + areaNote;
    }

    // Project > Rename Floor: source floor + new unique name (prefilled with it).
    [RelayCommand]
    private void RenameFloor()
    {
        if (_currentProject is null || _allFloors.Count == 0)
        {
            ImportResultMessage = "Open a project first.";
            return;
        }

        ShowRenameFloorDialog?.Invoke(new RenameFloorViewModel(
            StoreyOrderedFloorNames(),
            _currentFloor?.FloorName));
    }

    public void OnRenameFloorDialogClosed(RenameFloorViewModel vm)
    {
        if (!vm.DialogResult || _currentProject is null) return;

        var oldName = vm.SelectedFloor;
        var newName = vm.NewFloorName.Trim();
        if (string.IsNullOrEmpty(oldName) || newName.Length == 0) return;

        var floor = _allFloors.FirstOrDefault(f => f.FloorName == oldName);
        if (floor is null) return;
        if (oldName == newName) return; // no-op (case-only renames still proceed)

        // Backstop — the dialog already blocks a taken name (DB UNIQUE too).
        if (_allFloors.Any(f => f.Id != floor.Id
                && string.Equals(f.FloorName, newName, StringComparison.OrdinalIgnoreCase)))
            return;

        var wasCurrent = _currentFloor?.Id == floor.Id;

        // Floors are identified by NAME everywhere (combo, ShowFloor, staged editor
        // floor) — mutate the in-memory row, persist, then re-sync every name consumer.
        floor.FloorName = newName;
        _floorRepo.Update(floor);
        RefreshFloorNames();

        if (wasCurrent)
        {
            PreserveSelection(() => ShowFloor(newName));
        }
        else
        {
            RefreshSelectedWindowEditor(); // refills SelFloorNames with the new name
            RecalculateOutput();
        }

        ImportResultMessage = $"Renamed {oldName} to {newName}.";
    }

    // Project > Delete Floor: floor picker (defaults current) + in-dialog confirm.
    [RelayCommand]
    private void DeleteFloor()
    {
        if (_currentProject is null || _allFloors.Count == 0)
        {
            ImportResultMessage = "Open a project first.";
            return;
        }

        // Window counts per floor feed the confirm text ("…and its N window(s)…").
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in _allWindows.GroupBy(w => w.FloorId))
        {
            var name = _allFloors.FirstOrDefault(f => f.Id == g.Key)?.FloorName;
            if (name is not null) counts[name] = g.Count();
        }

        ShowDeleteFloorDialog?.Invoke(new DeleteFloorViewModel(
            StoreyOrderedFloorNames(),
            _currentFloor?.FloorName,
            counts));
    }

    public void OnDeleteFloorDialogClosed(DeleteFloorViewModel vm)
    {
        if (!vm.DialogResult || vm.DeletedFloorName is null || _currentProject is null) return;
        if (_allFloors.Count <= 1) return; // backstop: never delete the last floor

        var floor = _allFloors.FirstOrDefault(f => f.FloorName == vm.DeletedFloorName);
        if (floor is null) return;

        var windowCount = _allWindows.Count(w => w.FloorId == floor.Id);

        // Survivor: the next floor down the storey order, else the one above it.
        var ordered = StoreyOrderedFloorNames();
        var idx = ordered.IndexOf(floor.FloorName);
        var survivor = ordered.Skip(idx + 1).FirstOrDefault()
                    ?? ordered.Take(idx).LastOrDefault();
        if (survivor is null) return; // unreachable with >1 floor, but never go floorless

        // The selected window always lives on the current floor — if that floor goes,
        // clear the editor's target before its rows cascade away.
        if (_allWindows.FirstOrDefault(w => w.Id == SelectedWindowId)?.FloorId == floor.Id)
        {
            SelectedWindowId = -1;
            SelectedWindowCard = null;
            RefreshSelectedWindowEditor();
        }

        // The floor's plan image is a project-owned file — remove it with the row.
        try
        {
            if (!string.IsNullOrEmpty(floor.ImagePath) && File.Exists(floor.ImagePath))
                File.Delete(floor.ImagePath);
        }
        catch
        {
            // Locked/already-missing file — a harmless leftover beats a failed delete.
        }

        // Cascades WindowPlacements (Foreign Keys=True; ON DELETE CASCADE).
        _floorRepo.Delete(floor.Id);
        _allFloors.RemoveAll(f => f.Id == floor.Id);
        _allWindows.RemoveAll(w => w.FloorId == floor.Id);

        RefreshFloorNames();
        if (_currentFloor?.Id == floor.Id)
        {
            PreserveSelection(() => ShowFloor(survivor));
        }
        else
        {
            RecalculateOutput();
        }

        ImportResultMessage = windowCount > 0
            ? $"Deleted {floor.FloorName} ({windowCount} window(s) removed)."
            : $"Deleted {floor.FloorName}.";
    }

    // Reset to the fresh-app state so a deleted (or closed) building leaves no ghost
    // output/compass/editor behind. _currentProject is nulled FIRST so the property
    // partials (climate/orientation/floor) hit their null-guards and never persist.
    private void CloseCurrentProject()
    {
        _currentProject = null;
        _currentFloor = null;
        _allWindows.Clear();
        _allFloors.Clear();
        _rotationOffset = 0;
        RefreshFloorNames();

        HasProject = false;
        ClientName = string.Empty;
        ProjectName = string.Empty;
        BuildingName = string.Empty;
        SelectedClimateZone = "Zone 1";
        SelectedOrientation = "North";
        SelectedFloorName = "Ground Floor";
        FloorArea = string.Empty;
        IsMirrored = false;
        FloorPlanImage = null;

        SelectedWindowCard = null;
        SelectedWindowId = -1;
        RefreshSelectedWindowEditor();
        RefreshCompass();
        RecalculateOutput();

        _projectRepo.SetSetting("LastClient", "");
        _projectRepo.SetSetting("LastProject", "");
        _projectRepo.SetSetting("LastBuilding", "");
    }

    [RelayCommand]
    private void AddRange()
    {
        var vm = new AddRangeViewModel(_catalogueRepo);
        ShowAddRangeDialog?.Invoke(vm);
    }

    [RelayCommand]
    private void DeleteRange()
    {
        var vm = new DeleteRangeViewModel(_catalogueRepo);
        ShowDeleteRangeDialog?.Invoke(vm);
    }

    [RelayCommand]
    private void AddWindowType()
    {
        var vm = new AddWindowTypeViewModel(_catalogueRepo);
        ShowAddWindowTypeDialog?.Invoke(vm);
    }

    [RelayCommand]
    private void DeleteWindowType()
    {
        var vm = new DeleteWindowTypeViewModel(_catalogueRepo);
        ShowDeleteWindowTypeDialog?.Invoke(vm);
    }

    [RelayCommand]
    private void CopyOutput()
    {
        // Handled in code-behind via event; confirm in the bottom status bar
        RequestCopyToClipboard?.Invoke();
        ImportResultMessage = "Output copied to clipboard.";
    }

    public event Action? RequestCopyToClipboard;

    [RelayCommand]
    private void LoadImage()
    {
        RequestLoadFloorImage?.Invoke();
    }

    public event Action? RequestLoadFloorImage;

    // Copies the picked image into the app data folder so the source can move later
    public void SetFloorPlanImagePath(string? sourcePath)
    {
        if (_currentProject is null || _currentFloor is null || string.IsNullOrWhiteSpace(sourcePath))
            return;
        if (!File.Exists(sourcePath)) return;

        var imagesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FenCalc2", "Data", "Images");
        Directory.CreateDirectory(imagesDir);

        var dest = Path.Combine(imagesDir,
            $"{_currentProject.Id}_{_currentFloor.Id}{Path.GetExtension(sourcePath).ToLowerInvariant()}");
        File.Copy(sourcePath, dest, overwrite: true);

        _currentFloor.ImagePath = dest;
        _floorRepo.UpdateImagePath(_currentFloor.Id, dest);
        LoadFloorImage();
    }

    private void LoadFloorImage()
    {
        FloorPlanImage = null;
        var path = _currentFloor?.ImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            FloorPlanImage = new Bitmap(stream);
        }
        catch
        {
            FloorPlanImage = null;
        }
    }

    [RelayCommand]
    private void ImportOldDb()
    {
        // The legacy import opens the old FenCalc DB with the codec-enabled net46
        // System.Data.SQLite + a Windows-only SQLite.Interop.dll — nowhere else in the
        // app touches that stack, so Linux/macOS builds stay fully functional without it.
        if (!OperatingSystem.IsWindows())
        {
            ImportResultMessage =
                "Legacy import is only available on Windows (the old FenCalc database belongs to the Windows app).";
            return;
        }

        var result = DataMigrator.Migrate();
        ImportResultMessage = result.Summary;
        if (result.Success)
        {
            // Refresh the client list for Open Project
            HasImportedData = true;
        }
    }

    [ObservableProperty] private string _importResultMessage = string.Empty;
    [ObservableProperty] private bool _hasImportedData;

    public void CheckFirstLaunch()
    {
        if (DataMigrator.OldDbExists() && !_projectRepo.GetAll().Any())
        {
            // Old DB exists but new DB is empty — offer migration
            ImportResultMessage = "Old FenCalc database detected. Use Catalogue > Import Old Database to import your data.";
        }
        else if (CatalogueSeeder.SeededNow)
        {
            // Brand-new install (catalogue just seeded): point at the guide once.
            ImportResultMessage =
                "Welcome! Use Help > Getting Started: create a project, add a floor plan image, then add windows.";
        }
    }
}
