using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;
using FenCalc2.Models;

namespace FenCalc2.ViewModels;

public partial class AddWindowTypeViewModel : ViewModelBase
{
    private readonly CatalogueRepository _catalogueRepo;
    private List<GlazingPerformance> _allGlazing = new();
    private bool _loading;

    public AddWindowTypeViewModel(CatalogueRepository catalogueRepo)
    {
        _catalogueRepo = catalogueRepo;
        _loading = true;
        LoadData();
        _loading = false;
        ApplyRangeDefaults(SelectedRange);
    }

    [ObservableProperty] private string _typeName = string.Empty;
    [ObservableProperty] private string? _selectedRange;
    [ObservableProperty] private string? _selectedGlass;
    [ObservableProperty] private string? _selectedFrame;
    [ObservableProperty] private string _width = "0.900";
    [ObservableProperty] private string _height = "2.100";
    // U/SHGC prefill from the catalogue defaults; the user may alter before OK
    [ObservableProperty] private string _uValueText = string.Empty;
    [ObservableProperty] private string _shgcText = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _dialogResult;

    public ObservableCollection<string> Ranges { get; } = new();
    public ObservableCollection<string> GlassTypes { get; } = new();
    public ObservableCollection<string> FrameTypes { get; } = new();

    private void LoadData()
    {
        _allGlazing = _catalogueRepo.GetAllGlazing().ToList();
        foreach (var r in _catalogueRepo.GetRanges()) Ranges.Add(r.WinRange);
        foreach (var g in _allGlazing) GlassTypes.Add(g.Glass);
        foreach (var f in _catalogueRepo.GetAllFrames()) FrameTypes.Add(f.FrameType);
        if (Ranges.Count > 0) SelectedRange = Ranges[0];
    }

    partial void OnSelectedRangeChanged(string? value)
    {
        if (!_loading)
            ApplyRangeDefaults(value);
    }

    partial void OnSelectedGlassChanged(string? value) => UpdateEnergyValues();
    partial void OnSelectedFrameChanged(string? value) => UpdateEnergyValues();

    // Reseed glass/frame + U/SHGC on range change: default to the most popular
    // configuration already in the range, else Single Clear + Aluminium.
    private void ApplyRangeDefaults(string? range)
    {
        var types = range is null
            ? new List<WindowType>()
            : _catalogueRepo.GetTypesByRange(range).Where(t =>
                !string.IsNullOrWhiteSpace(t.WinGlass) && !string.IsNullOrWhiteSpace(t.WinFrame)).ToList();

        string? glass;
        string? frame;

        if (types.Count == 0)
        {
            glass = GlassTypes.Contains("Single Clear") ? "Single Clear" : GlassTypes.FirstOrDefault();
            frame = FrameTypes.Contains("Aluminium") ? "Aluminium" : FrameTypes.FirstOrDefault();
        }
        else
        {
            (glass, frame) = PickDefaultPair(types);
        }

        if (!string.IsNullOrEmpty(glass)) SelectedGlass = glass;
        if (!string.IsNullOrEmpty(frame)) SelectedFrame = frame;
        UpdateEnergyValues();
    }

    // Only the two divergent pairs compete (per decision):
    //   A = (top glass,    the frame most top-glass windows actually have)
    //   B = (the glass most top-frame windows actually have, top frame)
    // Higher exact-pair count wins; a tie keeps the pair containing the top glass.
    private static (string glass, string frame) PickDefaultPair(List<WindowType> types)
    {
        var topGlass = types.GroupBy(t => t.WinGlass!).OrderByDescending(g => g.Count()).First().Key;
        var topFrame = types.GroupBy(t => t.WinFrame!).OrderByDescending(g => g.Count()).First().Key;

        var glassOwnFrame = types.Where(t => t.WinGlass == topGlass)
            .GroupBy(t => t.WinFrame!).OrderByDescending(g => g.Count()).First().Key;
        var frameOwnGlass = types.Where(t => t.WinFrame == topFrame)
            .GroupBy(t => t.WinGlass!).OrderByDescending(g => g.Count()).First().Key;

        var supportA = types.Count(t => t.WinGlass == topGlass && t.WinFrame == glassOwnFrame);
        var supportB = types.Count(t => t.WinGlass == frameOwnGlass && t.WinFrame == topFrame);

        return supportA >= supportB
            ? (topGlass, glassOwnFrame)
            : (frameOwnGlass, topFrame);
    }

    private void UpdateEnergyValues()
    {
        if (string.IsNullOrEmpty(SelectedGlass) || string.IsNullOrEmpty(SelectedFrame))
        {
            UValueText = string.Empty;
            ShgcText = string.Empty;
            return;
        }

        var glazing = _allGlazing.FirstOrDefault(g => g.Glass == SelectedGlass);
        if (glazing is null)
        {
            UValueText = string.Empty;
            ShgcText = string.Empty;
            return;
        }

        var metal = Helpers.IsSteelOrAluminium(SelectedFrame);
        UValueText = (metal ? glazing.UValue_Steel : glazing.UValue_Other).ToString("F2");
        ShgcText = (metal ? glazing.SHGC_Steel : glazing.SHGC_Other).ToString("F2");
    }

    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(TypeName) || SelectedRange is null)
        {
            ErrorMessage = "Type name and range are required.";
            return;
        }

        var w = Helpers.ParseTolerant(Width);
        var h = Helpers.ParseTolerant(Height);
        if (w is null || h is null)
        {
            ErrorMessage = "Invalid width or height.";
            return;
        }

        var uValue = Helpers.ParseTolerant(UValueText);
        var shgc = Helpers.ParseTolerant(ShgcText);
        if ((!string.IsNullOrWhiteSpace(UValueText) && uValue is null) ||
            (!string.IsNullOrWhiteSpace(ShgcText) && shgc is null))
        {
            ErrorMessage = "U-Value and SHGC must be numbers, or blank for defaults.";
            return;
        }

        _catalogueRepo.InsertType(new WindowType
        {
            WinRange = SelectedRange,
            Name = TypeName.Trim(),
            WinWidth = w,
            WinHeight = h,
            WinGlass = SelectedGlass,
            WinFrame = SelectedFrame,
            UValue = uValue,
            SHGC = shgc
        });
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel() => DialogResult = false;
}
