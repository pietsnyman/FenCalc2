using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FenCalc2.Models;

namespace FenCalc2.Services;

/// <summary>
/// SANS 10400-XA:2026 (Edition 3) fenestration report — cl. 5.1 orientation,
/// 5.2 shading (P ≥ H × M, table 3), 5.3 per-storey ratio → table 4 band → weighted U
/// and grouped weighted SHGC with independent pass/fail per storey (no trade-offs).
///
/// Pure functions + one formatter: NO database access — the band/multiplier tables are
/// passed in (MainViewModel loads them via CatalogueRepository), which keeps this class
/// testable through the headless reflection harness. The legacy 2011 report lives
/// untouched in <see cref="OutputFormatter"/>.
///
/// Interpretations (also recorded in docs/plans/xa-2026.md):
/// - H (cl. 5.2.2) = base of the glazing → the same shadow-casting edge used for P,
///   i.e. window height + G — identical geometry to the legacy CalculatePH.
/// - The legacy g&gt;500 → P×0,5 quirk does NOT apply; 2026 is a clean P ≥ H×M.
/// - No site latitude ⇒ no shading credit (conservative: everything solar-facing counts
///   as unshaded, which applies the stricter SHGC limit).
/// - ShadingOverride (per window) wins over the automatic test.
/// - Southern-sector units are never shading-classified: their SHGC limit is "Any".
/// </summary>
public static class Fenestration2026
{
    // ---- reference-data lookups -----------------------------------------------

    /// <summary>Smallest band whose RatioMax covers this storey ratio (%).
    /// The "&gt;60 %" row is stored as 100 and acts as the catch-all.</summary>
    public static FenestrationBand? FindBand(IReadOnlyList<FenestrationBand> bands, double ratioPercent)
        => bands.FirstOrDefault(b => b.RatioMax >= ratioPercent) ?? bands.LastOrDefault();

    /// <summary>Table 3 multiplier for a site latitude (degrees South); null when the
    /// latitude is unset/invalid (no shading credit).</summary>
    public static double? MultiplierFor(IReadOnlyList<ShadingMultiplier> table, double? latitudeSouth)
    {
        if (latitudeSouth is not double lat || lat <= 0) return null;
        var row = table.FirstOrDefault(m => m.LatitudeMax >= lat);
        return row?.Multiplier;
    }

    // ---- sector + shading classification (5.2 / figure 2) -----------------------

    public static bool IsSolarSector(string trueDir) =>
        trueDir is "West" or "North West" or "North" or "North East" or "East";

    public static bool IsSouthern(string trueDir) =>
        trueDir is "South West" or "South" or "South East";

    /// <summary>H in mm: base of glazing → shadow-casting edge = window height + G.</summary>
    public static double ShadeHeightMm(WindowPlacement w) => w.WinHeight * 1000.0 + w.G;

    /// <summary>Cl. 5.2.2 automatic test P ≥ H × M, after any per-window override.</summary>
    public static bool IsShaded(WindowPlacement w, double? multiplier)
    {
        if (w.ShadingOverride is int forced) return forced == 1;
        if (multiplier is not double m) return false;      // no latitude → no credit
        if (w.P <= 0) return false;
        return w.P >= ShadeHeightMm(w) * m;
    }

    // ---- storey assessment (5.3) ------------------------------------------------

    public sealed class ShgcGroup
    {
        public string Name { get; init; } = string.Empty;
        public int Count { get; init; }
        public double WeightedShgc { get; init; }
        public double? Limit { get; init; }
        public bool Pass { get; init; }
        public bool Any => Limit is null;
    }

    public sealed class StoreyResult
    {
        public string Heading { get; init; } = string.Empty;
        public double? FloorArea { get; init; }
        public double FenestrationArea { get; init; }
        public double? RatioPercent { get; init; }
        public FenestrationBand? Band { get; init; }
        public double WeightedU { get; init; }
        public int UnitCount { get; init; }
        public List<ShgcGroup> Groups { get; } = new();
        public string? PrimaryFacade { get; init; }        // cl. 5.1, informational
        public bool LatitudeMissing { get; init; }
        public bool ReviewOnly => RatioPercent is null || Band is null;
        public bool Passes =>
            !ReviewOnly &&
            (Band!.MaxU is null || WeightedU <= Band.MaxU) &&
            Groups.All(g => g.Any || g.Pass);

        public string FailReason()
        {
            var reasons = new List<string>();
            if (RatioPercent is null || Band is null) return "floor area not set — ratio not assessed";
            if (Band.MaxU is double lim && WeightedU > lim)
                reasons.Add($"U {WeightedU:0.00} > {lim:0.00}");
            reasons.AddRange(Groups.Where(g => !g.Any && !g.Pass)
                .Select(g => $"{g.Name.ToLowerInvariant()} SHGC {g.WeightedShgc:0.000} > {g.Limit:0.00}"));
            return reasons.Count == 0 ? "—" : string.Join("; ", reasons);
        }
    }

    /// <summary>Assess one storey. windows carry their TRUE direction (resolve the plan
    /// slot with OutputFormatter.TrueOrientation before calling).</summary>
    public static StoreyResult AssessStorey(
        IReadOnlyList<FenestrationBand> bands,
        double? multiplier,
        string heading,
        double? floorArea,
        IReadOnlyList<(WindowPlacement W, string TrueDir)> windows)
    {
        var band = default(FenestrationBand);
        double? ratio = null;
        var fenArea = windows.Sum(x => SolarCalculator.CalculateWindowArea(x.W.WinWidth, x.W.WinHeight));
        if (floorArea is > 0)
            ratio = fenArea / floorArea.Value * 100.0;
        if (ratio is not null)
            band = FindBand(bands, ratio.Value);

        var uWeighted = windows.Count == 0 ? 0 :
            windows.Sum(x => SolarCalculator.CalculateWindowArea(x.W.WinWidth, x.W.WinHeight) * x.W.UValue) /
            windows.Sum(x => SolarCalculator.CalculateWindowArea(x.W.WinWidth, x.W.WinHeight));

        // Groups: solar-sector split shaded/unshaded; southern separate ("Any").
        var solarShaded = new List<(WindowPlacement W, double A, double Shgc)>();
        var solarUnshaded = new List<(WindowPlacement W, double A, double Shgc)>();
        var southern = new List<(WindowPlacement W, double A, double Shgc)>();
        foreach (var (w, dir) in windows)
        {
            var a = SolarCalculator.CalculateWindowArea(w.WinWidth, w.WinHeight);
            if (IsSouthern(dir)) southern.Add((w, a, w.SHGC));
            else if (IsSolarSector(dir))
            {
                if (IsShaded(w, multiplier)) solarShaded.Add((w, a, w.SHGC));
                else solarUnshaded.Add((w, a, w.SHGC));
            }
        }

        var result = new StoreyResult
        {
            Heading = heading,
            FloorArea = floorArea,
            FenestrationArea = fenArea,
            RatioPercent = ratio,
            Band = band,
            WeightedU = uWeighted,
            UnitCount = windows.Count,
            LatitudeMissing = multiplier is null,
            PrimaryFacade = PrimaryFacadeDirection(windows),
        };

        ShgcGroup Make(string name, List<(WindowPlacement W, double A, double Shgc)> list, double? limit)
        {
            var area = list.Sum(t => t.A);
            var wavg = area <= 0 ? 0 : list.Sum(t => t.A * t.Shgc) / area;
            return new ShgcGroup
            {
                Name = name,
                Count = list.Count,
                WeightedShgc = wavg,
                Limit = limit,
                Pass = limit is null || list.Count == 0 || wavg <= limit,
            };
        }

        // Band limits apply only when a band exists; ≤20 % band has NULL limits = Any.
        result.Groups.Add(Make("Shaded", solarShaded, band?.ShadedShgc));
        result.Groups.Add(Make("Unshaded", solarUnshaded, band?.UnshadedShgc));
        result.Groups.Add(Make("Southern", southern, band?.SouthernShgc));
        return result;
    }

    /// <summary>Cl. 5.1: the sector holding the largest combined fenestration area of
    /// the storey (informational — the report only prints it).</summary>
    public static string? PrimaryFacadeDirection(IReadOnlyList<(WindowPlacement W, string TrueDir)> windows)
    {
        var byDir = windows.GroupBy(x => x.TrueDir)
            .Select(g => new { Dir = g.Key, Area = g.Sum(x => SolarCalculator.CalculateWindowArea(x.W.WinWidth, x.W.WinHeight)) })
            .OrderByDescending(x => x.Area).ToList();
        return byDir.Count == 0 ? null : byDir[0].Dir;
    }

    // ---- report formatting -------------------------------------------------------

    private static string Fmt(bool three) => three ? "0.000" : "0.00";

    /// <summary>Build the XA:2026 report. Always per storey — cl. 5.3.5 Note 2 forbids
    /// trading storeys off, so the legacy Combine-floors toggle is intentionally absent.
    /// Standard-derived values (limits, M) print at the standard's own precision.</summary>
    public static string Format(
        FenCalcProject project,
        IReadOnlyList<Floor> floors,
        IReadOnlyList<WindowPlacement> allWindows,
        IReadOnlyList<FenestrationBand> bands,
        IReadOnlyList<ShadingMultiplier> multipliers,
        bool useThreeDecimals,
        bool capitalize)
    {
        var sb = new StringBuilder();
        var f = Fmt(useThreeDecimals);
        string Cap(string s) => capitalize ? s.ToUpperInvariant() : s;

        var multiplier = MultiplierFor(multipliers, project.Latitude);
        sb.AppendLine(Cap("Fenestration calculations:") + $" (SANS 10400-XA:2026)");
        sb.AppendLine("-----------------------------");
        // Values are capped too when Capitalize is on — a half-cased report (labels up,
        // "Zone 5"/"Edition 3"/"North" down) reads like a bug (user report, round 3).
        sb.AppendLine(Cap("Standard: SANS 10400-XA:2026 (Edition 3), cl. 5.1-5.3"));
        sb.AppendLine(Cap("Climate zone: ") + Cap(project.ClimateZone));
        var site = string.IsNullOrWhiteSpace(project.Town) ? "(custom)" : project.Town;
        var lat = project.Latitude is double l
            ? $"{l.ToString("0.000")}°S"
            : "NOT SET — no shading credited (5.2.2)";
        sb.AppendLine($"{Cap("Town")}: {Cap(site)}   {Cap("Latitude")}: {Cap(lat)}");
        if (project.Sccp) sb.AppendLine(Cap("Site is in the Southern Cape Condensation Problem Area (5.3.7)"));
        if (multiplier is double m) sb.AppendLine($"{Cap("Shading multiplier")} M = {m.ToString("0.00")}");
        sb.AppendLine();

        // Same floor ordering/filter as the legacy report (OutputFormatter is the single
        // source for FloorOrder — public there since the v3 plan-slot work).
        var ordered = floors
            .OrderBy(flo => OutputFormatter.FloorOrder(flo.FloorName))
            .Where(flo => allWindows.Any(w => w.FloorId == flo.Id) || flo.FloorArea is > 0)
            .ToList();

        var results = new List<(Floor Flo, StoreyResult R)>();
        foreach (var flo in ordered)
        {
            var wins = allWindows.Where(w => w.FloorId == flo.Id)
                .Select(w => (W: w, TrueDir: OutputFormatter.TrueOrientation(project, w.Orientation)))
                .ToList();
            var res = AssessStorey(bands, multiplier, flo.FloorName, flo.FloorArea, wins);
            results.Add((flo, res));

            sb.AppendLine(Cap(flo.FloorName) + ":");
            // Floor area always prints at 2 decimals — the established user decision from
            // the floors work ("floor area + ratio at 2 dp ALWAYS, both modes") also keeps
            // the 2026 report consistent with its own ratio line under the 3-dec toggle.
            if (flo.FloorArea is double fa) sb.AppendLine($"{Cap("Floor area")}: {fa.ToString("0.00")}");
            else sb.AppendLine(Cap("Floor area") + ": " + Cap("not set"));

            // Fenestration area per type (legacy layout, own copy — legacy stays frozen).
            foreach (var g in wins.GroupBy(w => w.W.WinType).OrderBy(g => g.Key))
            {
                var first = g.First().W;
                var area = SolarCalculator.CalculateWindowArea(first.WinWidth, first.WinHeight) * g.Count();
                sb.AppendLine($"{g.Key,-10}: {g.Count()} x {first.WinWidth:0.000} x {first.WinHeight:0.000} = {area.ToString(f)}");
            }
            sb.AppendLine($"{Cap("Total"),-10}: {res.FenestrationArea.ToString(f)}");
            sb.AppendLine();

            if (res.RatioPercent is double ratio && res.Band is FenestrationBand band)
            {
                sb.AppendLine($"{Cap("Window to floor area ratio")}: {res.FenestrationArea.ToString(f)} / {res.FloorArea:0.00} * 100 = {ratio.ToString("0.00")}%");
                var bandTxt = band.RatioMax >= 100 ? ">60 %" : $"≤ {band.RatioMax:0} %";
                var uTxt = band.MaxU is double bu ? bu.ToString("0.00") : "ANY";
                var sh = band.ShadedShgc is double bs ? bs.ToString("0.00") : "ANY";
                var us = band.UnshadedShgc is double bus ? bus.ToString("0.00") : "ANY";
                sb.AppendLine($"{Cap("Table 4 band")}: {bandTxt}   {Cap("Max U")}: {uTxt}   {Cap("SHGC shaded")}: {sh}   {Cap("unshaded")}: {us}   {Cap("southern")}: ANY");
            }
            else
            {
                sb.AppendLine(Cap("Window to floor area ratio") + ": " + Cap("NOT ASSESSED (set the floor area)"));
            }

            if (res.UnitCount > 0)
            {
                var uLine = res.Band?.MaxU is double ulim
                    ? $"{res.WeightedU.ToString(f)} ≤ {ulim.ToString("0.00")}  {(res.WeightedU <= ulim ? "OK" : "EXCEEDS")}"
                    : $"{res.WeightedU.ToString(f)}  (ANY)";
                sb.AppendLine($"{Cap("Weighted U-value")} ({res.UnitCount} {Cap("units")}): {uLine}");
                foreach (var g in res.Groups)
                {
                    if (g.Count == 0) continue;
                    var line = g.Any
                        ? $"{g.WeightedShgc.ToString(f)}  {Cap("(ANY solution)")}"
                        : $"{g.WeightedShgc.ToString(f)} ≤ {g.Limit!.Value.ToString("0.00")}  {(g.Pass ? "OK" : "EXCEEDS")}";
                    sb.AppendLine($"{Cap("Weighted SHGC")} — {Cap(g.Name)} ({g.Count}): {line}");
                }
            }

            // Per-unit shading evidence for the solar-facing units (5.2.2).
            var solar = wins.Where(x => IsSolarSector(x.TrueDir)).ToList();
            if (solar.Count > 0)
            {
                var shadeHdr = multiplier is double mm
                    ? $"Shading (5.2.2): P ≥ H × M, M = {mm.ToString("0.00")}"
                    : "Shading (5.2.2): site latitude NOT SET — no shading credit";
                sb.AppendLine(Cap(shadeHdr));
                foreach (var (w, dir) in solar)
                {
                    var h = ShadeHeightMm(w);
                    // Fixed-width numeric columns so the arrows line up when pasted in a
                    // monospace font (P width 4, H and the H×M result width 5, M is a
                    // constant 4 chars); the tail is kept verbatim for the harness checks.
                    var need = multiplier is double m2
                        ? $"H × M = {h.ToString("0"),5} × {m2.ToString("0.00")} = {(h * m2).ToString("0"),5}"
                        : "H × M = ?";
                    var state = IsShaded(w, multiplier) ? "SHADED" : "UNSHADED";
                    var forced = w.ShadingOverride is int ? Cap(" (override)") : "";
                    // Direction is capped BEFORE padding so the columns stay aligned;
                    // window type names are deliberately NOT capped (legacy rule:
                    // capitalize uppercases labels, not type names).
                    sb.AppendLine($"  {w.WinType,-10} {Cap(dir),-10} P {w.P,4} vs {need} → {state}{forced}");
                }
            }

            if (res.PrimaryFacade is string pf)
                sb.AppendLine($"{Cap("Primary facade (5.1)")}: {Cap(pf)}");
            sb.AppendLine($"{Cap("Storey result")}: {(res.Passes ? Cap("COMPLIES") : (res.ReviewOnly ? Cap("REVIEW") + $" ({Cap(res.FailReason())})" : Cap("DOES NOT COMPLY") + $" ({Cap(res.FailReason())})"))}");
            sb.AppendLine();
        }

        // Advisory footer (checks that are qualitative or outside this app's data).
        sb.AppendLine(Cap("Notes:"));
        sb.AppendLine(" - " + Cap("Whole-element U/SHGC per ANSI/NFRC 100/200 (5.3.2/5.3.3); centre-of-glass values not used."));
        sb.AppendLine(" - " + Cap("Air leakage of windows/doors/rooflights per SANS 613 (5.3.8)."));
        sb.AppendLine(" - " + Cap("One occupancy per storey assumed (5.3.6); multi-tenancy needs per-tenancy NFA."));
        if (project.Sccp)
            sb.AppendLine(" - " + Cap("5.3.7: U-values must not rely on single low-E glass alone in this area."));
        if (results.Any(r => r.R.ReviewOnly))
            sb.AppendLine(" - " + Cap("Set every floor's area for a complete assessment."));

        var fails = results.Where(r => !r.R.Passes && !r.R.ReviewOnly).Select(r => Cap(r.R.Heading)).ToList();
        var reviews = results.Where(r => r.R.ReviewOnly).Select(r => Cap(r.R.Heading)).ToList();
        sb.AppendLine();
        sb.AppendLine(Cap("Building result: ") +
            (fails.Count > 0 ? $"{Cap("DOES NOT COMPLY")} ({Cap("storeys")}: {string.Join(", ", fails)})"
             : reviews.Count > 0 ? $"{Cap("REVIEW")} ({Cap("storeys")}: {string.Join(", ", reviews)})"
             : Cap("COMPLIES")));
        return sb.ToString();
    }
}
