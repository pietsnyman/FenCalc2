using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FenCalc2.Models;

namespace FenCalc2.Services;

/// <summary>
/// External wall compliance — SANS 10400-XA:2026 cl. 5.5 (definitions from cl. 3).
/// Pure functions + one report builder: NO database access (layers arrive with their
/// library values resolved; the zone requirement row is passed in) so the whole thing is
/// harness-testable through the reflection rig.
///
/// Interpretations (docs/plans/walls.md):
/// - Total R (cl. 3.26) = Σ layer R INCLUDING the air films; films/cavity carry a FIXED
///   R from SANS 204 table F.2 (their thickness is physical only, ρ = 0 → no mass/C).
/// - C-value (cl. 3.x) = Σ c·ρ·d in J/m²K; CR (cl. 3.5) = C × R / 3600 (seconds → hours).
/// - Surface density (cl. 3.25) = Σ ρ·d; heavy ≥ 270 kg/m².
/// - Category 1 + heavy → table 6 does NOT apply (5.5.1 wording); the 5.5.2 single-leaf
///   ≥ 140 mm concession is checked and a cat-1 multi-leaf heavy wall is flagged REVIEW
///   (the clause text leaves it unaddressed — confirm with the local authority).
/// - Light walls (< 270) pass on R OR CR (5.5.3). Finish per table 6 note a; metal
///   break per 5.5.4 (any non-metal layer with R ≥ 0,2 counts as the break); attachment
///   per 5.5.5 is a declared mode, not a computed check.
/// </summary>
public static class WallCompliance
{
    public const double HeavyThreshold = 270.0;
    private const double MetalBreakR = 0.2;

    public sealed class Check
    {
        public string Name { get; init; } = string.Empty;
        public string Verdict { get; init; } = "N/A";   // PASS | FAIL | REVIEW | N/A
        public string Detail { get; init; } = string.Empty;
    }

    public sealed class Assessment
    {
        public double TotalR { get; init; }          // m².K/W incl. films
        public double CArea { get; init; }           // J/m²K
        public double CR { get; init; }              // hours
        public double SurfaceDensity { get; init; }  // kg/m²
        public bool IsHeavy => SurfaceDensity >= HeavyThreshold;
        public bool SingleLeaf { get; init; }
        public double SingleLeafMm { get; init; }
        public WallZoneRequirement? Req { get; init; }
        public List<Check> Checks { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool HasFail => Checks.Any(c => c.Verdict == "FAIL");
        public bool HasReview => Checks.Any(c => c.Verdict == "REVIEW") || Warnings.Count > 0;
        public string Result => HasFail ? "DOES NOT COMPLY" : HasReview ? "REVIEW" : "COMPLIES";
    }

    // ---- per-layer quantities ----------------------------------------------------

    /// <summary>Layer resistance m².K/W: fixed value for films/cavity, else d/λ.</summary>
    public static double LayerR(WallLayer l)
    {
        if (l.FixedR is double fr) return fr;
        if (l.Lambda is double lam && lam > 0 && l.ThicknessMm > 0) return (l.ThicknessMm / 1000.0) / lam;
        return 0;
    }

    /// <summary>Areal mass kg/m² (ρ·d); films/cavity have no density → 0.</summary>
    public static double LayerMass(WallLayer l) =>
        l.Density is double rho ? rho * l.ThicknessMm / 1000.0 : 0;

    /// <summary>Areal heat capacity J/m²K (c·ρ·d) — the per-layer C-value.</summary>
    public static double LayerC(WallLayer l) =>
        l.CSpec is double c && l.Density is double rho ? c * rho * l.ThicknessMm / 1000.0 : 0;

    // ---- assessment --------------------------------------------------------------

    public static Assessment Assess(IReadOnlyList<WallLayer> layers, WallAssembly wall, WallZoneRequirement? req)
    {
        var totalR = layers.Sum(LayerR);
        var cArea = layers.Sum(LayerC);
        var density = layers.Sum(LayerMass);
        var masonry = layers.Where(x => x.Category == "Masonry").ToList();
        var a = new Assessment
        {
            TotalR = totalR,
            CArea = cArea,
            CR = cArea * totalR / 3600.0,
            SurfaceDensity = density,
            Req = req,
            SingleLeaf = masonry.Count == 1,
            SingleLeafMm = masonry.Count == 1 ? masonry[0].ThicknessMm : 0,
        };

        AddOrderWarnings(a, layers);

        if (layers.Count == 0)
        {
            a.Checks.Add(new Check { Name = "Thermal", Verdict = "REVIEW", Detail = "wall has no layers yet" });
            return a;
        }

        // --- thermal (5.5.1 / 5.5.3 + tables 6/7)
        if (req is null)
        {
            a.Checks.Add(new Check { Name = "Thermal", Verdict = "REVIEW", Detail = "energy-zone requirement row missing — seed check" });
        }
        else if (a.IsHeavy)
        {
            if (wall.Category1)
            {
                // 5.5.1 excludes cat-1 buildings from table 6 (the 5.5.2 concession is
                // checked separately below, for any cat-1 single-leaf wall).
                a.Checks.Add(new Check
                {
                    Name = "Thermal (table 6)",
                    Verdict = "PASS",
                    Detail = "category 1 building — table 6 does not apply (5.5.1)",
                });
            }
            else
            {
                a.Checks.Add(new Check
                {
                    Name = "Thermal (table 6)",
                    Verdict = a.TotalR >= req.HeavyMinR ? "PASS" : "FAIL",
                    Detail = $"{a.TotalR:0.000} {(a.TotalR >= req.HeavyMinR ? "≥" : "<")} {req.HeavyMinR:0.00} m².K/W (heavy, ≥ 270 kg/m²)",
                });
                var needsCavity = req.HeavyConstruction.Contains("cavity", StringComparison.OrdinalIgnoreCase);
                if (needsCavity && !layers.Any(x => x.Category == "Cavity"))
                    a.Checks.Add(new Check
                    {
                        Name = "Construction (table 6)",
                        Verdict = "REVIEW",
                        Detail = $"zone {req.EnergyZone} deems a \"{req.HeavyConstruction}\" — no cavity layer found",
                    });
            }
        }
        else
        {
            var rPass = a.TotalR >= req.LightMinR;
            var crPass = a.CR >= req.LightMinCR;
            a.Checks.Add(new Check
            {
                Name = "Thermal (table 7)",
                Verdict = rPass || crPass ? "PASS" : "FAIL",
                Detail = $"R {a.TotalR:0.000} vs {req.LightMinR:0.00} {(rPass ? "✓" : "✗")} · CR {a.CR:0.0} h vs {req.LightMinCR:0} h {(crPass ? "✓" : "✗")} (light, either passes)",
            });
        }

        // --- category 1 (5.5.2): the ≥ 140 mm single-leaf rule applies to ANY cat-1
        // single-leaf masonry wall; cat-1 multi-leaf heavy walls are unaddressed by the
        // clause text (review); cat-1 without a masonry leaf falls through to table 7.
        if (wall.Category1)
        {
            if (a.SingleLeaf)
                a.Checks.Add(new Check
                {
                    Name = "Category 1 single leaf (5.5.2)",
                    Verdict = a.SingleLeafMm >= 140 ? "PASS" : "FAIL",
                    Detail = $"nominal {a.SingleLeafMm:0} mm {(a.SingleLeafMm >= 140 ? "≥" : "<")} 140 mm (plaster excluded)",
                });
            else if (a.IsHeavy)
                a.Checks.Add(new Check
                {
                    Name = "Category 1 (5.5.2)",
                    Verdict = "REVIEW",
                    Detail = "cat-1 building with a multi-leaf heavy wall — not addressed by 5.5.1/5.5.2, confirm with the local authority",
                });
            else
                a.Checks.Add(new Check
                {
                    Name = "Category 1 (5.5.2)",
                    Verdict = "N/A",
                    Detail = "no masonry single leaf — table 7 applies (5.5.3)",
                });
        }
        else
        {
            a.Checks.Add(new Check { Name = "Category 1 (5.5.2)", Verdict = "N/A", Detail = "not declared a category 1 building" });
        }

        // --- finish (table 6 note a)
        if (masonry.Count == 0)
        {
            a.Checks.Add(new Check { Name = "Finish (note a)", Verdict = "N/A", Detail = "non-masonry assembly" });
        }
        else if (masonry.All(m => m.PlasterFree))
        {
            a.Checks.Add(new Check { Name = "Finish (note a)", Verdict = "PASS", Detail = "plaster-free materials (FBX/FBS/FBA clay or high-density concrete) — plaster not required" });
        }
        else
        {
            var solids = layers.Where(x => x.Category != "Film").ToList();
            if (solids.Count == 0)
                a.Checks.Add(new Check { Name = "Finish (note a)", Verdict = "REVIEW", Detail = "no solid layers to check" });
            else
            {
                var outer = solids.First();
                var inner = solids.Last();
                var outerOk = outer.Category == "Plaster";
                var innerOk = inner.Category == "Plaster";
                a.Checks.Add(new Check
                {
                    Name = "Finish (note a)",
                    Verdict = outerOk && innerOk ? "PASS" : "FAIL",
                    Detail = outerOk && innerOk
                        ? "plastered internally and externally"
                        : $"plaster required on both faces — outside face is \"{outer.Name}\"{(outerOk ? "" : " (needs plaster)")}, inside face is \"{inner.Name}\"{(innerOk ? "" : " (needs plaster)")}",
                });
            }
        }

        // --- metal break (5.5.4)
        if (!layers.Any(x => x.Category == "Metal"))
        {
            a.Checks.Add(new Check { Name = "Metal break (5.5.4)", Verdict = "N/A", Detail = "no metal elements in the assembly" });
        }
        else
        {
            var brk = layers.FirstOrDefault(x => x.Category != "Metal" && LayerR(x) >= MetalBreakR);
            a.Checks.Add(new Check
            {
                Name = "Metal break (5.5.4)",
                Verdict = brk is not null ? "PASS" : "FAIL",
                Detail = brk is not null
                    ? $"thermal break ≥ 0,2 m².K/W present (\"{brk.Name}\", R {LayerR(brk):0.000})"
                    : "metal sheet/studs/tracks present but no layer with R ≥ 0,2 m².K/W between the metal elements",
            });
        }

        // --- attachment boundary (5.5.5) — declared, never computed
        a.Checks.Add(new Check
        {
            Name = "Attachment boundary (5.5.5)",
            Verdict = wall.AttachmentMode switch
            {
                "Included" => "PASS",
                "Excluded" => "REVIEW",
                _ => "N/A",
            },
            Detail = wall.AttachmentMode switch
            {
                "Included" => "attached spaces (garages/verandahs/…) declared included in the envelope",
                "Excluded" => "attached spaces declared excluded — confirm the separation does not compromise the envelope",
                _ => "no attachments declared",
            },
        });

        return a;
    }

    private static void AddOrderWarnings(Assessment a, IReadOnlyList<WallLayer> layers)
    {
        if (layers.Count == 0) return;

        var films = layers.Where(x => x.Category == "Film").ToList();
        if (films.Count == 0)
            a.Warnings.Add("no air film layers — total R misses the surface resistances (cl. 3.26); add the outside and inside air films");
        else
        {
            if (layers[0].Category != "Film")
                a.Warnings.Add($"the outermost layer is \"{layers[0].Name}\" — the outside air film should be first");
            if (layers[layers.Count - 1].Category != "Film")
                a.Warnings.Add($"the innermost layer is \"{layers[layers.Count - 1].Name}\" — the inside air film should be last");
            for (var i = 1; i < layers.Count - 1; i++)
                if (layers[i].Category == "Film")
                    a.Warnings.Add($"air film \"{layers[i].Name}\" is buried at position {i} — films belong at the two faces");
        }

        for (var i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            if (l.Category == "Cavity")
            {
                var leftOk = i > 0 && layers[i - 1].Category == "Masonry";
                var rightOk = i < layers.Count - 1 && layers[i + 1].Category == "Masonry";
                if (!leftOk || !rightOk)
                    a.Warnings.Add($"cavity \"{l.Name}\" at position {i} is not between two masonry leaves");
            }
            if (l.Category != "Film" && l.Category != "Cavity" && LayerR(l) <= 0)
                a.Warnings.Add($"layer \"{l.Name}\" contributes no resistance (no λ or fixed R) — check its properties");
        }
    }

    // ---- report ------------------------------------------------------------------

    /// <summary>The wall's own copyable report — deliberately separate from the
    /// fenestration report (user decision). Capitalize behaves like the main report.</summary>
    public static string BuildReport(
        WallAssembly wall,
        IReadOnlyList<WallLayer> layers,
        WallZoneRequirement? req,
        Assessment a,
        bool capitalize)
    {
        var sb = new StringBuilder();
        string Cap(string s) => capitalize ? s.ToUpperInvariant() : s;

        sb.AppendLine(Cap("External wall check:") + " (SANS 10400-XA:2026, cl. 5.5)");
        sb.AppendLine("------------------------------------");
        sb.AppendLine($"{Cap("Wall")}: {Cap(wall.Name)}   {Cap("Zone")}: {(req is not null ? Cap("Zone " + req.EnergyZone) : Cap("zone?"))}");
        sb.AppendLine($"{Cap("Category 1")}: {(wall.Category1 ? Cap("yes") : Cap("no"))}   {Cap("Attachments")}: {Cap(wall.AttachmentMode)}");
        sb.AppendLine();

        sb.AppendLine(Cap("Layers (outside → inside):"));
        sb.AppendLine($"  {"#",3}  {Cap("Material"),-42} {Cap("Thick"),7} {Cap("R"),9}");
        for (var i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            var r = LayerR(l);
            // The fixed-R star sits OUTSIDE the padded field so every comma stays in
            // one column (user report); layer names are capped with everything else.
            sb.AppendLine($"  {i,3}  {Cap(l.Name),-42} {l.ThicknessMm,6:0.#}mm {r,9:0.000}{(l.FixedR is not null ? "*" : "")}");
        }
        sb.AppendLine($"  {"",3}  {Cap("TOTAL"),-42} {layers.Sum(x => x.ThicknessMm),6:0.#}mm {a.TotalR,9:0.000}");
        sb.AppendLine();

        sb.AppendLine($"{Cap("Classification")}: {(a.IsHeavy ? Cap("Heavy wall") : Cap("Light wall"))} — {a.SurfaceDensity:0.0} kg/m² {(a.IsHeavy ? "≥" : "<")} 270");
        sb.AppendLine($"{Cap("Total R")}: {a.TotalR:0.000} m².K/W   {Cap("C-value")}: {a.CArea:0} J/m²K   {Cap("CR value")}: {a.CR:0.0} h");
        if (req is not null)
        {
            if (a.IsHeavy)
                sb.AppendLine($"{Cap("Required R")}: {req.HeavyMinR:0.00} m².K/W {Cap("(table 6)")}{(wall.Category1 ? " — " + Cap("not applied to category 1") : $"   {Cap("deemed-to-satisfy")}: {Cap(req.HeavyConstruction)}")}");
            else
                sb.AppendLine($"{Cap("Required")}: R {req.LightMinR:0.00} m².K/W {Cap("or")} CR {req.LightMinCR:0} h {Cap("(table 7)")}");
        }
        else
        {
            sb.AppendLine(Cap("Required R") + ": " + Cap("UNKNOWN — zone requirement missing"));
        }
        sb.AppendLine();

        sb.AppendLine(Cap("Checks:"));
        foreach (var c in a.Checks)
            sb.AppendLine($"  {Cap(c.Name),-30} {Cap(c.Verdict),-6}  {Cap(c.Detail)}");

        if (a.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Cap("Warnings:"));
            foreach (var w in a.Warnings) sb.AppendLine(" - " + Cap(w));
        }

        sb.AppendLine();
        sb.AppendLine(Cap("Result: ") + Cap(a.Result));
        sb.AppendLine();
        sb.AppendLine(Cap("Notes:"));
        sb.AppendLine(" - " + Cap("R-values per SANS 6946 constants (cl. 3.26); films/cavity per SANS 204 table F.2."));
        sb.AppendLine(" - " + Cap("Layer λ/ρ/c are editable per layer — enter manufacturer-tested values where available."));
        sb.AppendLine(" - " + Cap("* = fixed-resistance layer (no thickness-based calculation)."));
        if (req is not null && a.IsHeavy)
            sb.AppendLine(" - " + Cap("Table 6 construction note shown for information — confirm against your details."));
        return sb.ToString();
    }
}
