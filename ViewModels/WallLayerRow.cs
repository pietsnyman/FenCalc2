using System;
using FenCalc2.Models;
using FenCalc2.Services;

namespace FenCalc2.ViewModels;

/// <summary>
/// One row of the cross-section list: wraps a WallLayer, keeps every edit write-through
/// (repo update + re-assessment through the owner) and exposes the display strings the
/// row template binds (thickness, R, proportional bar width).
/// </summary>
public class WallLayerRow : ViewModelBase
{
    private readonly WallComplianceViewModel _owner;
    public WallLayer Data { get; }

    public WallLayerRow(WallComplianceViewModel owner, WallLayer data)
    {
        _owner = owner;
        Data = data;
    }

    public string Name
    {
        get => Data.Name;
        set
        {
            if (Data.Name == value) return;
            Data.Name = value;
            Persist();
            OnPropertyChanged();
        }
    }

    public string MaterialName => Data.MaterialName ?? "(no material)";
    public string? Category => Data.Category;
    public bool IsFilm => Data.Category == "Film";

    /// <summary>Thickness in mm (tolerant parse; invalid text keeps the last good
    /// value — the box shows what is typed, the calculator uses the stored number).</summary>
    public string ThicknessText
    {
        get => Data.ThicknessMm.ToString("0.##");
        set
        {
            if (IsFilm) return;
            var t = Helpers.ParseTolerant(value);
            if (t is null || t < 0) return;
            if (t != Data.ThicknessMm)
            {
                Data.ThicknessMm = t.Value;
                Persist();
                RefreshNumbers();
            }
            if (ThicknessText != value) OnPropertyChanged(nameof(ThicknessText));
        }
    }

    // Manufacturer overrides: empty string = back to the library value (NULL column).
    public string LambdaText
    {
        get => Data.LambdaOverride?.ToString() ?? string.Empty;
        set
        {
            if (Override(value, v => Data.LambdaOverride = v))
            {
                Persist();
                if (LambdaText != value) OnPropertyChanged(nameof(LambdaText));
            }
        }
    }

    public string DensityText
    {
        get => Data.DensityOverride?.ToString() ?? string.Empty;
        set
        {
            if (Override(value, v => Data.DensityOverride = v))
            {
                Persist();
                if (DensityText != value) OnPropertyChanged(nameof(DensityText));
            }
        }
    }

    public string CText
    {
        get => Data.COverride?.ToString() ?? string.Empty;
        set
        {
            if (Override(value, v => Data.COverride = v))
            {
                Persist();
                if (CText != value) OnPropertyChanged(nameof(CText));
            }
        }
    }

    private static bool Override(string text, Action<double?> assign)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            assign(null);
            return true;
        }
        var v = Helpers.ParseTolerant(text);
        if (v is null || v <= 0) return false;
        assign(v);
        return true;
    }

    public string LibraryLambdaText => Data.LibraryLambda?.ToString("0.###") ?? "—";

    public string ThicknessDisplay => $"{Data.ThicknessMm:0.#} mm";
    public string RDisplay => WallCompliance.LayerR(Data).ToString("0.000");
    public double BarWidth => Math.Clamp(Data.ThicknessMm * 0.55, 8, 130);

    private void Persist() => _owner.OnLayerEdited(this);

    /// <summary>Preset-chip path: update the thickness AND re-raise the display
    /// property unconditionally — a plain source change did not refresh the bound
    /// TextBox (user report), so the leaf notification is explicit here.</summary>
    public void SetThicknessFromPreset(string text)
    {
        if (IsFilm) return;
        var t = Helpers.ParseTolerant(text);
        if (t is null || t < 0) return;
        Data.ThicknessMm = t.Value;
        Persist();
        RefreshNumbers();
        OnPropertyChanged(nameof(ThicknessText));
    }

    private void RefreshNumbers()
    {
        OnPropertyChanged(nameof(ThicknessDisplay));
        OnPropertyChanged(nameof(BarWidth));
        OnPropertyChanged(nameof(RDisplay));
    }

    /// <summary>Raised after a material swap rewrites Category/Presets/overrides.</summary>
    public void NotifyMaterialChanged()
    {
        OnPropertyChanged(nameof(MaterialName));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(IsFilm));
        OnPropertyChanged(nameof(LambdaText));
        OnPropertyChanged(nameof(DensityText));
        OnPropertyChanged(nameof(CText));
        OnPropertyChanged(nameof(LibraryLambdaText));
        RefreshNumbers();
    }

    public override string ToString() => Data.Name;
}
