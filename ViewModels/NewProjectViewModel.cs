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
    private readonly CatalogueRepository _catalogueRepo;

    public NewProjectViewModel(ProjectRepository projectRepo, FloorRepository floorRepo, CatalogueRepository catalogueRepo)
    {
        _projectRepo = projectRepo;
        _floorRepo = floorRepo;
        _catalogueRepo = catalogueRepo;

        foreach (var c in _projectRepo.GetDistinctClients())
            Clients.Add(c);
        foreach (var t in _catalogueRepo.GetTowns())
            TownNames.Add(t.Town);

        // New buildings default to the CURRENT standard (2026); existing buildings keep
        // whatever edition they were created with (column default '2011').
        RefreshZoneList("2026");
    }

    [ObservableProperty] private string _clientName = string.Empty;
    [ObservableProperty] private string _projectName = string.Empty;
    [ObservableProperty] private string _buildingName = string.Empty;
    [ObservableProperty] private string _selectedClimateZone = "Zone 1";
    [ObservableProperty] private string _selectedOrientation = "North";
    // Assessment standard — 2026 is the default for NEW buildings (existing ones keep
    // '2011' through the column default; a report never changes edition silently).
    [ObservableProperty] private string _selectedStandardEdition = "2026";
    [ObservableProperty] private bool _isStandard2026 = true;
    // Annex C town (2026 only): an exact match fills zone + latitude + SCCP below
    [ObservableProperty] private string _townName = string.Empty;
    [ObservableProperty] private string _siteLatitude = string.Empty;
    [ObservableProperty] private bool _siteSccp;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _dialogResult;

    // Client suggestions + project suggestions of the CURRENT (existing) client; both
    // remain freely typeable — new values simply create new records.
    public ObservableCollection<string> Clients { get; } = new();
    public ObservableCollection<string> Projects { get; } = new();

    // Zone list is edition-dependent: 2011 = climatic 1-6, 2026 = energy 1-7 + 5H.
    public ObservableCollection<string> ClimateZones { get; } = new();
    public ObservableCollection<string> TownNames { get; } = new();
    public string[] StandardEditions { get; } = { "2011", "2026" };
    public string[] Orientations { get; } = MainViewModel.OrientationNames;

    private void RefreshZoneList(string edition)
    {
        var zones = (edition == "2026"
                ? _catalogueRepo.GetEnergyZoneList().Select(z => "Zone " + z)
                : MainViewModel.LegacyClimateZones)
            .ToList();
        if (zones.Count == 0) zones = MainViewModel.LegacyClimateZones.ToList(); // seeder guard

        var keep = SelectedClimateZone;
        ClimateZones.Clear();
        foreach (var z in zones) ClimateZones.Add(z);
        SelectedClimateZone = zones.Contains(keep) ? keep : zones[0];
    }

    partial void OnSelectedStandardEditionChanged(string value)
    {
        IsStandard2026 = value == "2026";
        RefreshZoneList(value);
    }

    partial void OnTownNameChanged(string value)
    {
        var match = string.IsNullOrWhiteSpace(value) ? null : _catalogueRepo.GetTown(value.Trim());
        if (match is null) return; // free text — zone/latitude get finished in the left panel
        var zone = "Zone " + match.EnergyZone;
        if (ClimateZones.Contains(zone)) SelectedClimateZone = zone;
        SiteLatitude = match.Latitude.ToString("0.000");
        SiteSccp = match.Sccp != 0;
    }

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
        var missing =
            (string.IsNullOrWhiteSpace(ClientName) ? "client, " : "") +
            (string.IsNullOrWhiteSpace(ProjectName) ? "project, " : "") +
            (string.IsNullOrWhiteSpace(BuildingName) ? "building, " : "");
        if (missing.Length > 0)
        {
            ErrorMessage = "Missing: " + missing.TrimEnd(' ', ',') + ".";
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
            ClimateZone = string.IsNullOrWhiteSpace(SelectedClimateZone)
                ? (ClimateZones.FirstOrDefault() ?? "Zone 1")
                : SelectedClimateZone,
            Orientation = SelectedOrientation,
            StandardEdition = SelectedStandardEdition,
            Town = string.IsNullOrWhiteSpace(TownName) ? null : TownName.Trim(),
            Latitude = Helpers.ParseTolerant(SiteLatitude),
            Sccp = SiteSccp
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
