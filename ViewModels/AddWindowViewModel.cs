using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;
using FenCalc2.Models;

namespace FenCalc2.ViewModels;

public partial class AddWindowViewModel : ViewModelBase
{
    private readonly CatalogueRepository _catalogueRepo;
    private List<GlazingPerformance> _allGlazing = new();
    private List<WindowType> _allTypes = new();

    public AddWindowViewModel(
        CatalogueRepository catalogueRepo,
        string orientation,
        IEnumerable<string> floorNames,
        string? defaultFloor,
        string? defaultRange = null)
    {
        _catalogueRepo = catalogueRepo;
        SelectedOrientation = orientation;
        foreach (var n in floorNames) FloorNames.Add(n);
        // Default = the floor shown on the compass; the user may retarget the new
        // window to any other floor of the building (never a fixed list).
        SelectedFloor = defaultFloor is not null && FloorNames.Contains(defaultFloor)
            ? defaultFloor
            : FloorNames.FirstOrDefault() ?? string.Empty;
        LoadCatalogueData(defaultRange);
    }

    // Floors of the open building (storey order) — where the new window will land.
    public ObservableCollection<string> FloorNames { get; } = new();

    [ObservableProperty]
    private string _selectedFloor = string.Empty;

    [ObservableProperty]
    private string _selectedOrientation;

    [ObservableProperty]
    private string _selectedWinType = string.Empty;

    [ObservableProperty]
    private string _selectedRange = string.Empty;

    [ObservableProperty]
    private string _selectedGlass = string.Empty;

    [ObservableProperty]
    private string _selectedFrame = string.Empty;

    [ObservableProperty]
    private string _width = "0.900";

    [ObservableProperty]
    private string _height = "2.100";

    [ObservableProperty]
    private string _p = "375";

    [ObservableProperty]
    private string _g = "160";

    [ObservableProperty]
    private string _uValue = string.Empty;

    [ObservableProperty]
    private string _shgc = string.Empty;

    [ObservableProperty]
    private string _roomName = string.Empty;

    [ObservableProperty]
    private bool _dialogResult;

    public ObservableCollection<string> WinTypes { get; } = new();       // types of the selected range
    public ObservableCollection<string> RangeNames { get; } = new();
    public ObservableCollection<string> GlassTypes { get; } = new();
    public ObservableCollection<string> FrameTypes { get; } = new();
    public string[] Orientations { get; } = MainViewModel.OrientationNames;

    private void LoadCatalogueData(string? defaultRange)
    {
        _allTypes = _catalogueRepo.GetAllTypes().ToList();
        _allGlazing = _catalogueRepo.GetAllGlazing().ToList();
        var frames = _catalogueRepo.GetAllFrames().ToList();

        foreach (var name in _allTypes.Where(t => !string.IsNullOrEmpty(t.WinRange))
                     .Select(t => t.WinRange!).Distinct())
            RangeNames.Add(name);
        foreach (var g in _allGlazing.Select(g => g.Glass))
            GlassTypes.Add(g);
        foreach (var f in frames.Select(f => f.FrameType))
            FrameTypes.Add(f);

        // Range default: caller-provided (most-represented in the project) or the
        // first known range. Selecting a range auto-selects its first type.
        var range = defaultRange;
        if (string.IsNullOrEmpty(range) || !RangeNames.Contains(range))
            range = RangeNames.Contains("Aluminium Top Hung") ? "Aluminium Top Hung" : RangeNames.FirstOrDefault();
        SelectedRange = range ?? string.Empty;
        if (GlassTypes.Count > 0) SelectedGlass = GlassTypes[0];
        if (FrameTypes.Count > 0) SelectedFrame = FrameTypes[0];

        UpdateEnergyValues();
    }

    partial void OnSelectedRangeChanged(string value)
    {
        // Only types of the selected range are offered; auto-select the first one
        WinTypes.Clear();
        foreach (var t in _allTypes.Where(t => t.WinRange == value).Select(t => t.Name).Distinct())
            WinTypes.Add(t);
        SelectedWinType = WinTypes.Count > 0 ? WinTypes[0] : string.Empty;
    }

    partial void OnSelectedGlassChanged(string value) => UpdateEnergyValues();
    partial void OnSelectedFrameChanged(string value) => UpdateEnergyValues();

    private void UpdateEnergyValues()
    {
        // Priority: manufacturer override stored on the selected window type, else the
        // GlazingPerformance default for the glass/frame combination. Runs again on ANY
        // change of Type/Glass/Frame, so manual edits are replaced by fresh values.
        var (u, shgc) = Helpers.ComputeUShgc(
            _allTypes.FirstOrDefault(t => t.Name == SelectedWinType),
            SelectedGlass,
            SelectedFrame,
            _allGlazing);
        UValue = u;
        Shgc = shgc;
    }

    partial void OnSelectedWinTypeChanged(string value)
    {
        var type = _allTypes.FirstOrDefault(t => t.Name == value);
        if (type is null) return;
        if (type.WinWidth.HasValue) Width = type.WinWidth.Value.ToString("F3");
        if (type.WinHeight.HasValue) Height = type.WinHeight.Value.ToString("F3");
        if (!string.IsNullOrEmpty(type.WinGlass)) SelectedGlass = type.WinGlass;
        if (!string.IsNullOrEmpty(type.WinFrame)) SelectedFrame = type.WinFrame;
        // If the type's glass/frame matched the current ones, no property-changed hook
        // fired — refresh manually so type overrides still apply.
        UpdateEnergyValues();
    }

    [RelayCommand]
    private void Ok()
    {
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }

    public WindowPlacement? ToWindowPlacement(int projectId, int floorId)
    {
        var w = Helpers.ParseTolerant(Width);
        var h = Helpers.ParseTolerant(Height);
        var p = Helpers.ParseTolerant(P);
        var g = Helpers.ParseTolerant(G);
        var u = Helpers.ParseTolerant(UValue);
        var shgc = Helpers.ParseTolerant(Shgc);
        if (w is null || h is null || p is null || g is null || u is null || shgc is null)
            return null;

        return new WindowPlacement
        {
            ProjectId = projectId,
            FloorId = floorId,
            WinType = SelectedWinType,
            WinWidth = w.Value,
            WinHeight = h.Value,
            Orientation = SelectedOrientation,
            WinGlass = SelectedGlass,
            WinFrame = SelectedFrame,
            UValue = u.Value,
            SHGC = shgc.Value,
            P = (int)Math.Round(p.Value),
            G = (int)Math.Round(g.Value),
            RoomName = string.IsNullOrWhiteSpace(RoomName) ? null : RoomName
        };
    }
}
