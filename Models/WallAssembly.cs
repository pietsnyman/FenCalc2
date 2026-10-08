namespace FenCalc2.Models;

/// <summary>One external wall assembly of a building (XA:2026 cl. 5.5) — a named,
/// reorderable stack of layers. Multiple assemblies per project.</summary>
public class WallAssembly
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Category 1 building per cl. 3.2 (manual declaration: occupancy/no
    /// basement/≤6,0 m support spacing/≤80 m² — the app only knows floor areas).</summary>
    public bool Category1 { get; set; }
    /// <summary>Cl. 5.5.5: None = no attachments declared · Included = garages/verandahs
    /// part of the envelope · Excluded = separated (must not compromise the envelope).</summary>
    public string AttachmentMode { get; set; } = "None";
}
