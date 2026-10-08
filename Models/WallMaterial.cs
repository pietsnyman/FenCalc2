namespace FenCalc2.Models;

/// <summary>A row of the seeded wall material library (tools/import_sans2026.py —
/// sources: CBA SA TN01, XA table 10, SANS 204 table F.2, generic fallbacks).</summary>
public class WallMaterial
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Film | Cavity | Plaster | Masonry | Insulation | Metal | Other — drives
    /// the layer colours, the finish check (footnote a) and the metal-break check.</summary>
    public string Category { get; set; } = string.Empty;
    public double? Lambda { get; set; }     // W/m.K; NULL for fixed-R films/cavity
    public double? Density { get; set; }    // kg/m3
    public double? CSpec { get; set; }      // J/kg.K (specific heat, for C-value/CR)
    public double? FixedR { get; set; }     // m2.K/W when the resistance is fixed
    public bool PlasterFree { get; set; }   // table 6 note a (FBX/FBS/FBA + hd concrete)
    public string Presets { get; set; } = string.Empty; // standard thicknesses (mm)
}
