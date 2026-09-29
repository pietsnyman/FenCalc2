using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;
using FenCalc2.Models;

namespace FenCalc2.ViewModels;

// Project > Delete Project: pick a building (no prefill), then confirm in-place —
// the first Delete click resolves the row and flips the dialog into ConfirmMode
// ("Yes, delete" is the only path to DialogResult=true). MainViewModel performs
// the actual delete (it owns the open-project state).
public partial class DeleteProjectViewModel : ViewModelBase
{
    private readonly ProjectRepository _projectRepo;

    public DeleteProjectViewModel(ProjectRepository projectRepo)
    {
        _projectRepo = projectRepo;

        foreach (var c in _projectRepo.GetDistinctClients())
            Clients.Add(c);
    }

    [ObservableProperty] private string? _selectedClient;
    [ObservableProperty] private string? _selectedProject;
    [ObservableProperty] private string? _selectedBuilding;
    [ObservableProperty] private bool _confirmMode;
    [ObservableProperty] private string _confirmText = string.Empty;
    [ObservableProperty] private bool _dialogResult;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<string> Clients { get; } = new();
    public ObservableCollection<string> Projects { get; } = new();
    public ObservableCollection<string> Buildings { get; } = new();

    public FenCalcProject? SelectedProject_ { get; private set; }

    // Cascade copied from OpenProjectViewModel (nothing prefilled here).
    partial void OnSelectedClientChanged(string? value)
    {
        Projects.Clear();
        Buildings.Clear();
        SelectedProject = null;
        SelectedBuilding = null;
        if (value is null) return;
        foreach (var p in _projectRepo.GetDistinctProjects(value))
            Projects.Add(p);
    }

    partial void OnSelectedProjectChanged(string? value)
    {
        Buildings.Clear();
        SelectedBuilding = null;
        if (value is null || SelectedClient is null) return;
        foreach (var b in _projectRepo.GetDistinctBuildings(SelectedClient, value))
            Buildings.Add(b);
    }

    // Phase 1 → phase 2: resolve the row, then ask for confirmation.
    [RelayCommand]
    private void Delete()
    {
        ErrorMessage = string.Empty;

        if (SelectedClient is null || SelectedProject is null || SelectedBuilding is null)
        {
            ErrorMessage = "Please select client, project and building.";
            return;
        }

        SelectedProject_ = _projectRepo.GetByClientAndProject(SelectedClient, SelectedProject)
            .FirstOrDefault(p => p.BuildingName == SelectedBuilding);
        if (SelectedProject_ is null)
        {
            ErrorMessage = "Project not found.";
            return;
        }

        ConfirmText = $"Delete '{SelectedProject_.BuildingName}' " +
            $"({SelectedProject_.ClientName} / {SelectedProject_.ProjectName})? " +
            "This cannot be undone.";
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
