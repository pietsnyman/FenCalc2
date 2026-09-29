using Avalonia.Styling;

namespace FenCalc2;

// Custom light-based theme variants for View > Theme. Each name MUST match a
// ThemeDictionaries x:Key entry in App.axaml (full palette copies live there).
// Base is Light so FluentTheme control resources resolve through the variant chain.
public static class AppThemes
{
    public const string OliveName = "Olive";
    public const string BurntOrangeName = "BurntOrange";
    public const string MulberryName = "Mulberry";
    public const string SageName = "Sage";

    public static readonly ThemeVariant Olive = new(OliveName, ThemeVariant.Light);
    public static readonly ThemeVariant BurntOrange = new(BurntOrangeName, ThemeVariant.Light);
    public static readonly ThemeVariant Mulberry = new(MulberryName, ThemeVariant.Light);
    public static readonly ThemeVariant Sage = new(SageName, ThemeVariant.Light);

    // Maps a persisted AppSettings["Theme"] value to its custom variant;
    // null = not a color theme (Light/Dark/System handled by the caller).
    public static ThemeVariant? FromName(string? setting) => setting switch
    {
        OliveName => Olive,
        BurntOrangeName => BurntOrange,
        MulberryName => Mulberry,
        SageName => Sage,
        _ => null,
    };
}
