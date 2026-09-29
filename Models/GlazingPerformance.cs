namespace FenCalc2.Models;

public class GlazingPerformance
{
    public string Glass { get; set; } = string.Empty;
    public double UValue_Steel { get; set; }
    public double SHGC_Steel { get; set; }
    public double UValue_Other { get; set; }
    public double SHGC_Other { get; set; }
}
