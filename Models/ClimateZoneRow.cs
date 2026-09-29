namespace FenCalc2.Models;

public class ClimateZoneRow
{
    public int Zone { get; set; }
    public string PH { get; set; } = string.Empty;
    public double North { get; set; }
    public double NorthEast { get; set; }
    public double East { get; set; }
    public double SouthEast { get; set; }
    public double South { get; set; }
    public double SouthWest { get; set; }
    public double West { get; set; }
    public double NorthWest { get; set; }
}
