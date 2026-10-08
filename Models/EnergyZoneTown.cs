namespace FenCalc2.Models;

/// <summary>Annex C table C.1 row of SANS 10400-XA:2026 (via Data/seed/xa2026.sql).</summary>
public class EnergyZoneTown
{
    public string Town { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    /// <summary>'1'..'7' or '5H' — the XA:2026 energy zone (NOT the legacy 6-zone scheme).</summary>
    public string EnergyZone { get; set; } = string.Empty;
    /// <summary>Southern Cape Condensation Problem Area flag (cl. 5.3.7).</summary>
    public int Sccp { get; set; }
}
