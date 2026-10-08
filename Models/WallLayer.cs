namespace FenCalc2.Models;

/// <summary>
/// One layer of a wall assembly. Pos0 = outermost (outside face). Library* fields come
/// from the WallMaterials JOIN; the *Override columns store manufacturer-tested values
/// (NULL = use the library). The effective value helpers below are what the calculator
/// consumes.
/// </summary>
public class WallLayer
{
    public int Id { get; set; }
    public int WallId { get; set; }
    public int Pos { get; set; }
    public int? MaterialId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double ThicknessMm { get; set; }

    // Manufacturer overrides (per layer instance)
    public double? LambdaOverride { get; set; }
    public double? DensityOverride { get; set; }
    public double? COverride { get; set; }

    // Resolved from the material library (NULL when no library row applies)
    public double? LibraryLambda { get; set; }
    public double? LibraryDensity { get; set; }
    public double? LibraryC { get; set; }
    public double? FixedR { get; set; }
    public bool PlasterFree { get; set; }
    public string? Presets { get; set; }
    public string? MaterialName { get; set; }

    public double? Lambda => LambdaOverride ?? LibraryLambda;
    public double? Density => DensityOverride ?? LibraryDensity;
    public double? CSpec => COverride ?? LibraryC;
}
