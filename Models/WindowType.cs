namespace FenCalc2.Models;

public class WindowType
{
    public int Id { get; set; }
    public string WinRange { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? WinWidth { get; set; }
    public double? WinHeight { get; set; }
    public string? WinGlass { get; set; }
    public string? WinFrame { get; set; }
    // Manufacturer-tested overrides (null = use GlazingPerformance defaults)
    public double? UValue { get; set; }
    public double? SHGC { get; set; }
}
