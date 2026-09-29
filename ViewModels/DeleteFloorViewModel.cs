using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FenCalc2.ViewModels;

// Project > Delete Floor: pick a floor (defaults to the current one), then confirm
// in-place — the first Delete click resolves the target, checks the last-floor guard
// and flips the dialog into ConfirmMode ("Yes, delete" is the only path to
// DialogResult=true). MainViewModel performs the actual delete (cascade, image file,
// survivor selection — it owns the open-building state).
public partial class DeleteFloorViewModel : ViewModelBase
{
    private readonly IReadOnlyDictionary<string, int> _windowCounts;

    public DeleteFloorViewModel(IEnumerable<string> floorNames, string? currentFloor,
        IReadOnlyDictionary<string, int> windowCounts)
    {
        foreach (var n in floorNames) FloorNames.Add(n);
        _windowCounts = windowCounts;
        SelectedFloor = currentFloor is not null && FloorNames.Contains(currentFloor)
            ? currentFloor
            : FloorNames.FirstOrDefault();
    }

    // Storey-ordered names of the open building — the only floors that may be deleted.
    public ObservableCollection<string> FloorNames { get; } = new();

    [ObservableProperty] private string? _selectedFloor;
    [ObservableProperty] private bool _confirmMode;
    [ObservableProperty] private string _confirmText = string.Empty;
    [ObservableProperty] private bool _dialogResult;
    [ObservableProperty] private string _errorMessage = string.Empty;

    // Set when phase 1 resolves the target; MainViewModel deletes it.
    public string? DeletedFloorName { get; private set; }

    partial void OnSelectedFloorChanged(string? value)
    {
        // Clear a stale error from a previous attempt.
        ErrorMessage = string.Empty;
    }

    // Phase 1 → phase 2: guard + resolve, then ask for confirmation.
    [RelayCommand]
    private void Delete()
    {
        ErrorMessage = string.Empty;

        if (FloorNames.Count <= 1)
        {
            ErrorMessage = "A building must keep at least one floor.";
            return;
        }

        if (string.IsNullOrEmpty(SelectedFloor))
        {
            ErrorMessage = "Please select the floor to delete.";
            return;
        }

        DeletedFloorName = SelectedFloor;
        var windows = _windowCounts.TryGetValue(SelectedFloor!, out var c) ? c : 0;
        ConfirmText = windows > 0
            ? $"Delete '{SelectedFloor}' and its {windows} window(s)? This cannot be undone."
            : $"Delete '{SelectedFloor}'? This cannot be undone.";
        ConfirmMode = true;
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        DialogResult = true;
    }

    [RelayCommand]
    private void Back()
    {
        ConfirmMode = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
