namespace FenCalc2.ViewModels;

public class WindowCard
{
    public int PlacementId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}
