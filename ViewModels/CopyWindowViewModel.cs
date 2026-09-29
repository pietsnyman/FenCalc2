using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FenCalc2.ViewModels;

// One checkbox row of the Copy to… dialog (a floor or a direction).
public partial class CopyOption : ObservableObject
{
    public CopyOption(string name, bool isChecked)
    {
        Name = name;
        _isChecked = isChecked;
    }

    public string Name { get; }

    [ObservableProperty] private bool _isChecked;
}

// Selected Window > Copy to…: multi-select floors AND directions in one go. Prechecked
// with the source window's own floor and its current TRUE direction, so an untouched
// OK duplicates it in place. MainViewModel inserts every floor×direction combination.
public partial class CopyWindowViewModel : ViewModelBase
{
    public CopyWindowViewModel(IEnumerable<string> floorNames, string? currentFloor, string currentDirection)
    {
        foreach (var n in floorNames)
            Floors.Add(new CopyOption(n, n == currentFloor));
        foreach (var d in MainViewModel.OrientationNames)
            Directions.Add(new CopyOption(d, d == currentDirection));
    }

    public ObservableCollection<CopyOption> Floors { get; } = new();
    public ObservableCollection<CopyOption> Directions { get; } = new();

    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _dialogResult;

    public IEnumerable<string> SelectedFloors => Floors.Where(f => f.IsChecked).Select(f => f.Name);

    public IEnumerable<string> SelectedDirections => Directions.Where(d => d.IsChecked).Select(d => d.Name);

    [RelayCommand]
    private void Ok()
    {
        ErrorMessage = string.Empty;
        if (!SelectedFloors.Any())
        {
            ErrorMessage = "Select at least one floor.";
            return;
        }
        if (!SelectedDirections.Any())
        {
            ErrorMessage = "Select at least one direction.";
            return;
        }
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
