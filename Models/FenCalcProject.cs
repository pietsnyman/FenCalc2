namespace FenCalc2.Models;

public class FenCalcProject
{
    public int Id { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ClimateZone { get; set; } = "Zone 1";
    // North anchor: which plan direction (image-relative) is true North
    public string Orientation { get; set; } = "North";
    // Persisted compass-view state: label-ring rotation and mirrored image flag
    public int RotationOffset { get; set; }
    public bool IsMirrored { get; set; }
}
