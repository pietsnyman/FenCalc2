namespace FenCalc2.Models;

public class Floor
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string FloorName { get; set; } = string.Empty;
    public double? FloorArea { get; set; }
    public string? ImagePath { get; set; }
}
