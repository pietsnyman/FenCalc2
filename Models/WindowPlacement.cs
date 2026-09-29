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
    public string? RoomName { get; set; }
}
