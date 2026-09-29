using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FenCalc2.ViewModels;

// Project > Rename Floor: pick a floor (defaults to the current one) and type a new
// name — unique against the building EXCEPT the floor being renamed (so a case-only
// fix or no-op close passes). The dialog only validates — MainViewModel owns the open
// building and performs the rename (floor lists and the compass follow the name).
public partial class RenameFloorViewModel : ViewModelBase
{
    public RenameFloorViewModel(IEnumerable<string> floorNames, string? currentFloor)
    {
        foreach (var n in floorNames) FloorNames.Add(n);
        SelectedFloor = currentFloor is not null && FloorNames.Contains(currentFloor)
            ? currentFloor
            : FloorNames.FirstOrDefault();
        NewFloorName = SelectedFloor ?? string.Empty;
    }

    // Storey-ordered names of the open building — the only floors that may be renamed.
    public ObservableCollection<string> FloorNames { get; } = new();

    [ObservableProperty] private string? _selectedFloor;
    [ObservableProperty] private string _newFloorName = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _dialogResult;

    partial void OnSelectedFloorChanged(string? value)
    {
        // Switching the source re-seeds the name box with that floor's name (rename
        // semantics — unlike Duplicate, which keeps a typed name across source picks).
        NewFloorName = value ?? string.Empty;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void Ok()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrEmpty(SelectedFloor))
        {
            ErrorMessage = "Please select the floor to rename.";
            return;
        }

        var name = NewFloorName.Trim();
        if (name.Length == 0)
        {
            ErrorMessage = "Enter a new name for the floor.";
            return;
        }

        if (FloorNames.Any(f => !string.Equals(f, SelectedFloor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"A floor named '{name}' already exists.";
            return;
        }

        NewFloorName = name; // trimmed copy for the callback
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
