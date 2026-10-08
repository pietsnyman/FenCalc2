namespace FenCalc2.Models;

/// <summary>
/// One row of table 3 of SANS 10400-XA:2026 (cl. 5.2.2): shading projection multiplier
/// M for sites up to LatitudeMax degrees South. LatitudeMax 999 = the "&gt;32" row.
/// </summary>
public class ShadingMultiplier
{
    public double LatitudeMax { get; set; }
    public double Multiplier { get; set; }
}
