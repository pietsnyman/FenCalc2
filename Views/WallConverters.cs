using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FenCalc2.Views;

// Wall-page value converters. Category brushes resolve from the single theme-INDEPENDENT
// layer token set (App.axaml outer dictionary) → always safe. Verdict brushes resolve
// theme-VARIANT semantic tokens, and since the wall page became a TAB (theme menu
// reachable while it is displayed) WallComplianceViewModel.RefreshThemedBrushes()
// re-raises the check rows on every theme switch via MainViewModel.RefreshThemedBrushes.
public static class WallConverters
{
    private static IBrush Resolve(string key, string fallbackHex)
    {
        if (Application.Current is { } app
            && app.TryFindResource(key, app.ActualThemeVariant, out var value)
            && value is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    /// <summary>Material category → the colour-coded cross-section bar.</summary>
    public static readonly IValueConverter CategoryBrush =
        new FuncValueConverter<string?, IBrush>(category => category switch
        {
            "Film" => Resolve("LayerFilmBrush", "#BFD0DE"),
            "Cavity" => Resolve("LayerCavityBrush", "#F2F3F5"),
            "Plaster" => Resolve("LayerPlasterBrush", "#EBDCB2"),
            "Masonry" => Resolve("LayerMasonryBrush", "#C67B5C"),
            "Insulation" => Resolve("LayerInsulationBrush", "#E5A8B8"),
            "Metal" => Resolve("LayerMetalBrush", "#9AA4AE"),
            _ => Resolve("LayerOtherBrush", "#C9BFAD"),
        });

    /// <summary>Check verdict → semantic text colour (PASS/FAIL/REVIEW/N/A).</summary>
    public static readonly IValueConverter VerdictBrush =
        new FuncValueConverter<string?, IBrush>(verdict => verdict switch
        {
            "PASS" => Resolve("SuccessBrush", "#15803D"),
            "FAIL" => Resolve("DangerBrush", "#B42318"),
            "REVIEW" => Resolve("WarningBrush", "#B45309"),
            _ => Resolve("TextSecondaryBrush", "#6B7280"),
        });

    /// <summary>Used to disable the thickness box for air films (fixed-R layers).</summary>
    public static readonly IValueConverter InverseBool =
        new FuncValueConverter<bool, bool>(b => !b);
}
