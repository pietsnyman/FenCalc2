namespace FenCalc2.Models;

public class WindowPlacement
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int FloorId { get; set; }
    public string WinType { get; set; } = string.Empty;
    public double WinWidth { get; set; }
    public double WinHeight { get; set; }
    public string Orientation { get; set; } = string.Empty;
    public string WinGlass { get; set; } = string.Empty;
    public string WinFrame { get; set; } = string.Empty;
    public double UValue { get; set; }
    public double SHGC { get; set; }
    public int P { get; set; } = 375;
    public int G { get; set; } = 160;
    // XA:2026 cl. 5.2.2 shading classification: null = automatic (P >= H x M by the
    // project latitude), 1 = count as shaded, 0 = count as unshaded (shutters, blinds
    // and screens that satisfy 5.2.1 b) but cannot be proven by projection alone).
    public int? ShadingOverride { get; set; }
    public string? RoomName { get; set; }
}
