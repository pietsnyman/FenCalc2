using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;
using FenCalc2.Models;

namespace FenCalc2.ViewModels;

public partial class OpenProjectViewModel : ViewModelBase
{
    private readonly ProjectRepository _projectRepo;

    public OpenProjectViewModel(ProjectRepository projectRepo,
        string? prefillClient = null, string? prefillProject = null, string? prefillBuilding = null)
    {
        _projectRepo = projectRepo;
        LoadClients();
        Prefill(prefillClient, prefillProject, prefillBuilding);
    }

    private void Prefill(string? client, string? project, string? building)
    {
        if (string.IsNullOrEmpty(client) || !Clients.Contains(client)) return;
        SelectedClient = client;

        if (string.IsNullOrEmpty(project) || !Projects.Contains(project)) return;
        SelectedProject = project;

        if (!string.IsNullOrEmpty(building) && Buildings.Contains(building))
            SelectedBuilding = building;
    }

    [ObservableProperty] private string? _selectedClient;
    [ObservableProperty] private string? _selectedProject;
    [ObservableProperty] private string? _selectedBuilding;
    [ObservableProperty] private bool _dialogResult;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<string> Clients { get; } = new();
    public ObservableCollection<string> Projects { get; } = new();
    public ObservableCollection<string> Buildings { get; } = new();

    private void LoadClients()
    {
        Clients.Clear();
        foreach (var c in _projectRepo.GetDistinctClients())
            Clients.Add(c);
    }

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

    [RelayCommand]
    private void Ok()
    {
        if (SelectedClient is null || SelectedProject is null || SelectedBuilding is null)
        {
            ErrorMessage = "Please select client, project and building.";
            return;
        }

        var projects = _projectRepo.GetByClientAndProject(SelectedClient, SelectedProject);
        SelectedProject_ = projects.FirstOrDefault(p => p.BuildingName == SelectedBuilding);
        if (SelectedProject_ is null)
        {
            ErrorMessage = "Project not found.";
            return;
        }

        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }

    public FenCalcProject? SelectedProject_ { get; private set; }
}
