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
    // Report standards edition: "2011" (legacy formula set — the default so existing
    // projects keep their output byte-identical) or "2026" (XA:2026 cl. 5.1–5.3).
    public string StandardEdition { get; set; } = "2011";
    // Site data for the 2026 report: latitude drives the table 3 shading multiplier,
    // Sccp the 5.3.7 note. Town may be a free-text name not in the annex C list.
    public string? Town { get; set; }
    public double? Latitude { get; set; }
    public bool Sccp { get; set; }
    /// <summary>Energy zone for the walls page (tables 6/7) — independent of
    /// StandardEdition: on 2011 buildings ClimateZone is a climatic zone whose number
    /// means something else, so walls carry their own zone.</summary>
    public string? WallEnergyZone { get; set; }
}
