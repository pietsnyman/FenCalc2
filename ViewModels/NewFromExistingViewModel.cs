using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;
using FenCalc2.Models;

namespace FenCalc2.ViewModels;

// Project > New From Existing: pick an existing building (source cascade, prefilled
// from the Last* settings), name a destination, and on OK duplicate the whole
// building — project row (names replaced, zone/anchor/compass copied), floors,
// floor-plan image files and every window placement — so it can be edited at once.
public partial class NewFromExistingViewModel : ViewModelBase
{
    private readonly ProjectRepository _projectRepo;
    private readonly FloorRepository _floorRepo;
    private readonly WindowPlacementRepository _windowRepo;

    public NewFromExistingViewModel(ProjectRepository projectRepo, FloorRepository floorRepo,
        WindowPlacementRepository windowRepo, string? prefillClient = null,
        string? prefillProject = null, string? prefillBuilding = null)
    {
        _projectRepo = projectRepo;
        _floorRepo = floorRepo;
        _windowRepo = windowRepo;

        foreach (var c in _projectRepo.GetDistinctClients())
        {
            SourceClients.Add(c);
            DestClients.Add(c);
        }

        Prefill(prefillClient, prefillProject, prefillBuilding);
    }

    // Source: existing building to copy from (plain dropdowns — must resolve to one row)
    [ObservableProperty] private string? _sourceClient;
    [ObservableProperty] private string? _sourceProject;
    [ObservableProperty] private string? _sourceBuilding;

    // Destination: client/project editable (dropdown + typeable-new), building typed
    [ObservableProperty] private string? _destClient;
    [ObservableProperty] private string? _destProject;
    [ObservableProperty] private string _destBuilding = string.Empty;

    [ObservableProperty] private bool _dialogResult;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<string> SourceClients { get; } = new();
    public ObservableCollection<string> SourceProjects { get; } = new();
    public ObservableCollection<string> SourceBuildings { get; } = new();
    public ObservableCollection<string> DestClients { get; } = new();
    public ObservableCollection<string> DestProjects { get; } = new();

    public FenCalcProject? CreatedProject { get; private set; }

    // Same graceful cascade-prefill as OpenProjectViewModel (stale Last* just stops).
    private void Prefill(string? client, string? project, string? building)
    {
        if (string.IsNullOrEmpty(client) || !SourceClients.Contains(client)) return;
        SourceClient = client;

        if (string.IsNullOrEmpty(project) || !SourceProjects.Contains(project)) return;
        SourceProject = project;

        if (!string.IsNullOrEmpty(building) && SourceBuildings.Contains(building))
            SourceBuilding = building;
    }

    // Source cascade: client -> projects -> buildings. Every source pick mirrors into
    // the destination (destination edits stay independent — its handlers never feed
    // back into the source). The destination BUILDING never mirrors: it must be a
    // new typed name.
    partial void OnSourceClientChanged(string? value)
    {
        SourceProjects.Clear();
        SourceBuildings.Clear();
        SourceProject = null;
        SourceBuilding = null;
        if (value is not null)
            foreach (var p in _projectRepo.GetDistinctProjects(value))
                SourceProjects.Add(p);

        DestClient = value;
    }

    partial void OnSourceProjectChanged(string? value)
    {
        SourceBuildings.Clear();
        SourceBuilding = null;
        if (value is not null && SourceClient is not null)
            foreach (var b in _projectRepo.GetDistinctBuildings(SourceClient, value))
                SourceBuildings.Add(b);

        DestProject = value;
    }

    // Destination client: refill its project suggestions (mirroring from the source
    // runs this too; the source project is set right afterwards). Typing a brand-new
    // client simply yields no suggestions until the user picks/types a project.
    partial void OnDestClientChanged(string? value)
    {
        DestProjects.Clear();
        DestProject = null;
        if (value is not null)
            foreach (var p in _projectRepo.GetDistinctProjects(value))
                DestProjects.Add(p);
    }

    [RelayCommand]
    private void Ok()
    {
        ErrorMessage = string.Empty;

        if (SourceClient is null || SourceProject is null || SourceBuilding is null)
        {
            ErrorMessage = "Please select a source client, project and building.";
            return;
        }

        var destClient = (DestClient ?? string.Empty).Trim();
        var destProject = (DestProject ?? string.Empty).Trim();
        var destBuilding = DestBuilding.Trim();
        if (destClient.Length == 0 || destProject.Length == 0 || destBuilding.Length == 0)
        {
            ErrorMessage = "All destination fields are required — the building name must be typed.";
            return;
        }

        var src = _projectRepo.GetByClientAndProject(SourceClient, SourceProject)
            .FirstOrDefault(p => p.BuildingName == SourceBuilding);
        if (src is null)
        {
            ErrorMessage = "Source building not found.";
            return;
        }

        if (_projectRepo.Exists(destBuilding, destClient, destProject))
        {
            ErrorMessage = "A project with these details already exists.";
            return;
        }

        // New project row: destination names, everything else copied from the source
        var clone = new FenCalcProject
        {
            BuildingName = destBuilding,
            ClientName = destClient,
            ProjectName = destProject,
            ClimateZone = src.ClimateZone,
            Orientation = src.Orientation,
            // Same assessment basis as the source: edition + site (2026 report inputs)
            StandardEdition = src.StandardEdition,
            Town = src.Town,
            Latitude = src.Latitude,
            Sccp = src.Sccp
        };
        clone.Id = _projectRepo.Insert(clone);
        _projectRepo.UpdateCompassState(clone.Id, src.RotationOffset, src.IsMirrored);

        var imagesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FenCalc2", "Data", "Images");

        var srcWindows = _windowRepo.GetByProject(src.Id).ToList();
        foreach (var sf in _floorRepo.GetByProject(src.Id))
        {
            var newFloorId = _floorRepo.Insert(new Floor
            {
                ProjectId = clone.Id,
                FloorName = sf.FloorName,
                FloorArea = sf.FloorArea
            });

            // Copy the floor-plan image so the clone is self-contained; a missing
            // source file just leaves the new floor without an image.
            if (!string.IsNullOrEmpty(sf.ImagePath) && File.Exists(sf.ImagePath))
            {
                Directory.CreateDirectory(imagesDir);
                var destImage = Path.Combine(imagesDir,
                    $"{clone.Id}_{newFloorId}{Path.GetExtension(sf.ImagePath).ToLowerInvariant()}");
                File.Copy(sf.ImagePath, destImage, overwrite: true);
                _floorRepo.UpdateImagePath(newFloorId, destImage);
            }

            foreach (var w in srcWindows.Where(w => w.FloorId == sf.Id))
            {
                _windowRepo.Insert(new WindowPlacement
                {
                    ProjectId = clone.Id,
                    FloorId = newFloorId,
                    WinType = w.WinType,
                    WinWidth = w.WinWidth,
                    WinHeight = w.WinHeight,
                    Orientation = w.Orientation,
                    WinGlass = w.WinGlass,
                    WinFrame = w.WinFrame,
                    UValue = w.UValue,
                    SHGC = w.SHGC,
                    P = w.P,
                    G = w.G,
                    RoomName = w.RoomName
                });
            }
        }

        CreatedProject = _projectRepo.GetById(clone.Id);
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
