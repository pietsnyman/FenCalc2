namespace FenCalc2.Models;

/// <summary>Merged table 6 (heavy) + table 7 (light) requirements of one energy zone
/// (XA:2026 cl. 5.5 — seeded from docs/sans.txt via tools/import_sans2026.py).</summary>
public class WallZoneRequirement
{
    public string EnergyZone { get; set; } = string.Empty;
    public double HeavyMinR { get; set; }              // table 6: density >= 270 kg/m²
    public string HeavyConstruction { get; set; } = string.Empty; // '50 mm cavity wall' | 'Collar jointed wall'
    public double LightMinR { get; set; }              // table 7: density < 270
    public double LightMinCR { get; set; }             // CR in hours (cl. 3.5) — pass on R OR CR
}
