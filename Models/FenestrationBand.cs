namespace FenCalc2.Models;

/// <summary>
/// One band of table 4 of SANS 10400-XA:2026 (cl. 5.3.4/5.3.5): limits for a storey
/// whose fenestration ratio is ≤ RatioMax %. NULL = "Any solution".
/// RatioMax 100 represents the standard's ">60 %" row.
/// </summary>
public class FenestrationBand
{
    public double RatioMax { get; set; }
    public double? MaxU { get; set; }
    public double? ShadedShgc { get; set; }
    public double? UnshadedShgc { get; set; }
    public double? SouthernShgc { get; set; }
}
