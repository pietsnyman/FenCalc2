using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FenCalc2.Views;

// Compass tile backgrounds: the tile whose CURRENT true-direction label is North gets
// a stronger blue (it follows North as the ring rotates/anchors); all others get the
// plain tile blue. Brushes are resolved from the App palette at convert time so both
// variants follow the active theme — MainViewModel.RefreshThemedBrushes() re-raises
// the LabelX bindings after a theme switch to force a re-run.
public static class CompassConverters
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

    public static readonly IValueConverter NorthTileBrush =
        new FuncValueConverter<string?, IBrush>(label =>
            label == "North"
                ? Resolve("CompassNorthBrush", "#BBDEFB")
                : Resolve("CompassTileBrush", "#E3F2FD"));

    // Flip Image button label: names the action AND shows the current flip state, so a
    // mirrored plan image is never silently mirrored ("Unflip Image" = it is flipped now).
    public static readonly IValueConverter FlipImageLabel =
        new FuncValueConverter<bool, string>(mirrored => mirrored ? "Unflip Image" : "Flip Image");
}
