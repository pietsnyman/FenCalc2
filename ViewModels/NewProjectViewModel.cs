using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;
using FenCalc2.Models;

namespace FenCalc2.ViewModels;

public partial class NewProjectViewModel : ViewModelBase
{
    private readonly ProjectRepository _projectRepo;
    private readonly FloorRepository _floorRepo;

    public NewProjectViewModel(ProjectRepository projectRepo, FloorRepository floorRepo)
    {
        _projectRepo = projectRepo;
        _floorRepo = floorRepo;

        foreach (var c in _projectRepo.GetDistinctClients())
            Clients.Add(c);
    }

    [ObservableProperty] private string _clientName = string.Empty;
    [ObservableProperty] private string _projectName = string.Empty;
    [ObservableProperty] private string _buildingName = string.Empty;
    [ObservableProperty] private string _selectedClimateZone = "Zone 1";
    [ObservableProperty] private string _selectedOrientation = "North";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _dialogResult;

    // Client suggestions + project suggestions of the CURRENT (existing) client; both
    // remain freely typeable — new values simply create new records.
    public ObservableCollection<string> Clients { get; } = new();
    public ObservableCollection<string> Projects { get; } = new();

    public string[] ClimateZones { get; } = { "Zone 1", "Zone 2", "Zone 3", "Zone 4", "Zone 5", "Zone 6" };
    public string[] Orientations { get; } = MainViewModel.OrientationNames;

    public void OnClientTextChanged()
    {
        Projects.Clear();
        if (Clients.Contains(ClientName))
            foreach (var p in _projectRepo.GetDistinctProjects(ClientName))
                Projects.Add(p);
    }

    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(ClientName) ||
            string.IsNullOrWhiteSpace(ProjectName) ||
            string.IsNullOrWhiteSpace(BuildingName))
        {
            ErrorMessage = "All fields are required.";
            return;
        }

        if (_projectRepo.Exists(BuildingName, ClientName, ProjectName))
        {
            ErrorMessage = "A project with these details already exists.";
            return;
        }

        var project = new FenCalcProject
        {
            BuildingName = BuildingName.Trim(),
            ClientName = ClientName.Trim(),
            ProjectName = ProjectName.Trim(),
            ClimateZone = SelectedClimateZone,
            Orientation = SelectedOrientation
        };

        var projectId = _projectRepo.Insert(project);
        project.Id = projectId;

        // Create default Ground Floor
        _floorRepo.Insert(new Floor
        {
            ProjectId = projectId,
            FloorName = "Ground Floor"
        });

        CreatedProject = project;
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }

    public FenCalcProject? CreatedProject { get; private set; }
}
