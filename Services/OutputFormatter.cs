using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FenCalc2.Models;

namespace FenCalc2.Services;

public class OutputFormatter
{
    // WindowPlacement.Orientation stores the image-relative plan slot; solar exposure
    // needs the TRUE compass direction, derived from the project's north anchor.
    private static readonly string[] Directions =
        { "North", "North East", "East", "South East", "South", "South West", "West", "North West" };

    internal static string TrueOrientation(FenCalcProject project, string planSlot)
    {
        var slotIdx = Array.IndexOf(Directions, planSlot);
        if (slotIdx < 0) return planSlot;
        var anchorIdx = Array.IndexOf(Directions, project.Orientation);
        if (anchorIdx < 0) anchorIdx = 0;
        // Rotation offset is part of the saved compass state, so it affects solar math too
        return Directions[(slotIdx + anchorIdx + project.RotationOffset) % 8];
    }

    private readonly SolarCalculator _calculator;
    private bool _capitalize;
    private string _decFormat = "0.00";

    // Floor area and the window-to-floor-area ratio are ALWAYS shown at 2 decimals
    // (user spec) — they never follow the 2/3-decimal display toggle. This also covers
    // the floor-area operands in the Constants lines. The ratio's window-area
    // numerator is not a floor area and keeps _decFormat.
    private const string AreaFormat = "0.00";

    // Every computed value is rounded to 3 decimals BEFORE it is used or shown, in
    // BOTH modes; the 2/3-dec toggle only controls display precision.
    private static double R3(double v) => Math.Round(v, 3);

    public OutputFormatter(SolarCalculator calculator)
    {
        _calculator = calculator;
    }

    public string Format(
        FenCalcProject project,
        List<Floor> floors,
        List<WindowPlacement> allWindows,
        bool useThreeDecimals,
        bool capitalize,
        bool combineFloors = false)
    {
        _capitalize = capitalize;
        _decFormat = useThreeDecimals ? "0.000" : "0.00";

        var sb = new StringBuilder();
        var zoneNumber = SolarCalculator.GetZoneNumber(project.ClimateZone);

        // Header
        sb.AppendLine(Cap("Fenestration Calculations:"));
        sb.AppendLine(Cap("-------------------------"));
        if (!string.IsNullOrWhiteSpace(project.ClimateZone))
            sb.AppendLine(Cap($"Climate Zone: {project.ClimateZone}"));

        // Sort floors by predefined order
        var sortedFloors = floors
            .OrderBy(f => FloorOrder(f.FloorName))
            .ToList();

        // A floor block prints when it has windows or a floor area. Headings
        // (name + dashes) only appear when more than one block prints — a
        // single-storey building doesn't need "GROUND FLOOR:" labels. (Counting
        // printed blocks rather than windowed floors keeps labels when a second
        // floor has an area but no windows yet — two blocks need distinguishing.)
        var printedFloors = sortedFloors
            .Where(f => allWindows.Any(w => w.FloorId == f.Id) || f.FloorArea.HasValue)
            .ToList();

        // Calculation blocks: one per printed floor (default — each floor functions
        // individually) or ONE whole-building block when the user combines floors
        // (summed floor area, every window pooled, no per-floor headings — the
        // per-section totals then span the whole building).
        List<Block> blocks;
        if (combineFloors)
        {
            double? totalArea = sortedFloors.Any(f => f.FloorArea.HasValue)
                ? sortedFloors.Where(f => f.FloorArea.HasValue).Sum(f => f.FloorArea!.Value)
                : null;
            blocks = printedFloors.Count == 0
                ? new List<Block>()
                : new List<Block> { new Block(null, totalArea, allWindows.ToList()) };
        }
        else
        {
            blocks = printedFloors
                .Select(f => new Block(f.FloorName, f.FloorArea,
                    allWindows.Where(w => w.FloorId == f.Id).ToList()))
                .ToList();
        }

        bool showFloorHeadings = !combineFloors && printedFloors.Count > 1;

        foreach (var block in blocks)
        {
            var floorWindows = block.Windows;
            var floorArea = block.Area;

            // Floor heading (this single blank also separates consecutive floors —
            // the old firstFloor flag added a second one)
            sb.AppendLine();
            if (showFloorHeadings)
            {
                sb.AppendLine(Cap(block.Heading!) + ":");
                sb.AppendLine(new string('-', block.Heading!.Length));
            }

            // Floor area — always 2 decimals
            if (floorArea.HasValue)
            {
                sb.AppendLine(Cap($"Floor area: {floorArea.Value.ToString(AreaFormat)}"));
            }
            sb.AppendLine();
            sb.AppendLine(Cap("Fenestration area:"));

            // Window areas by type
            var types = floorWindows
                .GroupBy(w => w.WinType)
                .OrderBy(g => g.Key)
                .ToList();

            if (types.Count > 0)
            {
                int longestName = types.Max(g => g.Key.Length);
                var areaLines = new List<(string name, int count, double width, double height, double totalArea)>();

                foreach (var type in types)
                {
                    var first = type.First();
                    int count = type.Count();
                    double area = R3(SolarCalculator.CalculateWindowArea(first.WinWidth, first.WinHeight) * count);
                    areaLines.Add((type.Key, count, first.WinWidth, first.WinHeight, area));
                }

                int longestArea = areaLines.Max(a => a.totalArea.ToString(_decFormat).Length);

                foreach (var line in areaLines)
                {
                    var name = line.name.PadRight(longestName);
                    var areaStr = line.totalArea.ToString(_decFormat).PadLeft(longestArea);
                    sb.AppendLine($"{name} : {line.count} x {line.width.ToString(_decFormat)} x {line.height.ToString(_decFormat)} = {areaStr}");
                }

                // Total window area
                double totalWindowArea = areaLines.Sum(a => a.totalArea);
                var totalStr = totalWindowArea.ToString(_decFormat);
                var lastLine = areaLines.Last();
                var lastLineText = $"{lastLine.name.PadRight(longestName)} : {lastLine.count} x {lastLine.width.ToString(_decFormat)} x {lastLine.height.ToString(_decFormat)} = {lastLine.totalArea.ToString(_decFormat).PadLeft(longestArea)}";
                var padLen = lastLineText.Length - totalStr.Length - 6; // "Total:" = 6 chars
                if (padLen < 1) padLen = 1;
                sb.AppendLine(Cap("Total:") + new string(' ', padLen) + totalStr);

                // Floor area ratio — floor area and result always 2 decimals
                if (floorArea.HasValue && floorArea.Value > 0)
                {
                    sb.AppendLine();
                    var ratio = R3(SolarCalculator.CalculateFloorAreaRatio(totalWindowArea, floorArea.Value));
                    sb.AppendLine(Cap($"Window to floor area ratio: {totalWindowArea.ToString(_decFormat)} / {floorArea.Value.ToString(AreaFormat)} * 100 = {ratio.ToString(AreaFormat)}%"));
                }
            }

            // Constants (zone factors keep their natural table precision; results follow
            // the toggle; the floor-area operand is always 2 decimals)
            if (floorArea.HasValue)
            {
                var (condFactor, shgFactor) = SolarCalculator.GetZoneConstants(project.ClimateZone);
                var condVal = R3(condFactor * floorArea.Value).ToString(_decFormat);
                var shgVal = R3(shgFactor * floorArea.Value).ToString(_decFormat);
                int maxLen = Math.Max(condVal.Length, shgVal.Length);

                sb.AppendLine();
                sb.AppendLine(Cap("Constants:") );
                sb.AppendLine(Cap($"Conductance: {floorArea.Value.ToString(AreaFormat)} x {condFactor.ToString("0.0").Replace(".", ",")}  = {condVal.PadLeft(maxLen)}"));

                var shgLabel = SolarCalculator.GetZoneNumber(project.ClimateZone) == 3 ? "S.H.G." : "SHG";
                sb.AppendLine(Cap($"{shgLabel}        : {floorArea.Value.ToString(AreaFormat)} x {shgFactor.ToString("0.00").Replace(".", ",")} = {shgVal.PadLeft(maxLen)}"));
            }

            // Solar heat gain per orientation
            sb.AppendLine();
            sb.AppendLine(Cap("Solar heat gain: (Area x SHGC x Solar e factor)"));

            var orientations = new[] { "North", "North East", "East", "South East", "South", "South West", "West", "North West" };

            // Row data is built for all directions first so column widths can be
            // calculated globally (aligned monospaced output, commas line up).
            var solarGroups = new List<(string Dir, List<SolarRow> Rows)>();

            foreach (var orient in orientations)
            {
                var orientWindows = floorWindows
                    .Where(w => TrueOrientation(project, w.Orientation) == orient)
                    .ToList();
                if (orientWindows.Count == 0) continue;

                    var groups = orientWindows
                    .GroupBy(w => $"{w.WinType}|{w.WinWidth}|{w.WinHeight}|{w.SHGC}|{w.P}|{w.G}")
                    .Select(g =>
                    {
                        var first = g.First();
                        int count = g.Count();
                        double ph = R3(SolarCalculator.CalculatePH(first.P, first.G, first.WinHeight));
                        double factor = R3(_calculator.GetSolarExposureFactor(ph, zoneNumber, orient));
                        double heatGain = R3(first.WinWidth * first.WinHeight * first.SHGC * factor * count);
                        double area = R3(count * first.WinWidth * first.WinHeight);
                        // P/H display: "P/winH", or "P/2/winH" when G > 500 (P is halved)
                        int winH = first.G + (int)Math.Round(first.WinHeight * 1000);
                        string expr = first.G > 500 ? $"{first.P}/2/{winH}" : $"{first.P}/{winH}";
                        return new SolarRow(first.WinType, count, expr, ph, area, factor, first.SHGC, heatGain);
                    })
                    .ToList();

                solarGroups.Add((orient, groups));
            }

            if (solarGroups.Count > 0)
            {
                var allRows = solarGroups.SelectMany(gr => gr.Rows).ToList();

                // Column widths are computed globally across ALL directions so that the
                // name/P/H columns and every number line up in the monospaced output.
                int nameW = allRows.Max(r => (r.Count > 1 ? $"{r.Count} x " : "").Length + r.Name.Length);
                int phExprW = allRows.Max(r => r.Expr.Length);
                int phW = allRows.Max(r => r.PH.ToString(_decFormat).Length);
                int areaW = allRows.Max(r => r.Area.ToString(_decFormat).Length);
                int factorW = allRows.Max(r => r.Factor.ToString(_decFormat).Length);
                int shgcW = allRows.Max(r => r.SHGC.ToString(_decFormat).Length);
                int heatW = allRows.Max(r => r.Heat.ToString(_decFormat).Length);

                // Segment layout per row — the tail chain IS the section formula:
                // [name][" : P/H = " + expr][" = " + ph][" : " area " x " shgc " x " factor " = " heat]
                int factorColStart = nameW + 9 + phExprW + 3 + phW;
                int heatColEnd = factorColStart + 3 + areaW + 3 + shgcW + 3 + factorW + 3 + heatW;

                void AppendRow(string nameCol, string phExprSeg, string ph, string area, string shgc, string factor, string heat)
                {
                    var line = new StringBuilder();
                    line.Append(nameCol.PadRight(nameW));
                    line.Append(" : P/H = ");
                    line.Append(phExprSeg.PadRight(phExprW));
                    line.Append(" = ").Append(ph.PadLeft(phW));
                    line.Append(" : ").Append(area.PadLeft(areaW));
                    line.Append(" x ").Append(shgc.PadLeft(shgcW));
                    line.Append(" x ").Append(factor.PadLeft(factorW));
                    line.Append(" = ").Append(heat.PadLeft(heatW));
                    sb.AppendLine(line.ToString());
                }

                foreach (var (dir, rows) in solarGroups)
                {
                    // Direction line directly under the section header / previous
                    // direction: NO blank lines inside the solar section (user spec).
                    // Rows read as "Area x SHGC x Solar e factor = heat".
                    sb.AppendLine(Cap(dir) + ":");

                    foreach (var r in rows)
                    {
                        var nameCol = (r.Count > 1 ? $"{r.Count} x " : "") + r.Name;
                        AppendRow(nameCol, r.Expr,
                            r.PH.ToString(_decFormat), r.Area.ToString(_decFormat),
                            r.SHGC.ToString(_decFormat), r.Factor.ToString(_decFormat),
                            r.Heat.ToString(_decFormat));
                    }
                }

                // One grand total for the whole block, aligned with the heat column
                var totalStr = R3(allRows.Sum(r => r.Heat)).ToString(_decFormat);
                var totalLine = new StringBuilder(Cap("Total:"));
                var pad = heatColEnd - totalLine.Length - totalStr.Length;
                if (pad < 1) pad = 1;
                totalLine.Append(' ', pad).Append(totalStr);
                sb.AppendLine(totalLine.ToString());
            }

            // Conductance
            sb.AppendLine();
            sb.AppendLine(Cap("Conductance:"));

            var condTypes = floorWindows
                .GroupBy(w => w.WinType)
                .OrderBy(g => g.Key)
                .ToList();

            if (condTypes.Count > 0)
            {
                int longestName = condTypes.Max(g => g.Key.Length);
                var condLines = new List<(string name, int count, double area, double uValue, double total)>();

                foreach (var type in condTypes)
                {
                    var first = type.First();
                    int count = type.Count();
                    double area = R3(SolarCalculator.CalculateWindowArea(first.WinWidth, first.WinHeight));
                    double cond = R3(SolarCalculator.CalculateConductance(count, area, first.UValue));
                    condLines.Add((type.Key, count, area, first.UValue, cond));
                }

                int longestTotal = condLines.Max(c => c.total.ToString(_decFormat).Length);

                foreach (var line in condLines)
                {
                    var name = line.name.PadRight(longestName);
                    var totalStr = line.total.ToString(_decFormat).PadLeft(longestTotal);
                    sb.AppendLine($"{name} : {line.count} x {line.area.ToString(_decFormat)} x {line.uValue.ToString(_decFormat)}   = {totalStr}");
                }

                // Conductance total
                double condGrandTotal = condLines.Sum(c => c.total);
                var condTotalStr = condGrandTotal.ToString(_decFormat);
                var lastCondLine = condLines.Last();
                var lastCondText = $"{lastCondLine.name.PadRight(longestName)} : {lastCondLine.count} x {lastCondLine.area.ToString(_decFormat)} x {lastCondLine.uValue.ToString(_decFormat)}   = {lastCondLine.total.ToString(_decFormat).PadLeft(longestTotal)}";
                var padLen3 = lastCondText.Length - condTotalStr.Length - 6;
                if (padLen3 < 1) padLen3 = 1;
                sb.AppendLine(Cap("Total:") + new string(' ', padLen3) + condTotalStr);
            }
        }

        return sb.ToString();
    }

    private sealed record SolarRow(string Name, int Count, string Expr, double PH, double Area, double Factor, double SHGC, double Heat);

    // One printed section: heading (null = no floor heading, i.e. combined mode),
    // floor area (null = this block has none) and the windows it calculates over.
    private sealed record Block(string? Heading, double? Area, List<WindowPlacement> Windows);

    private string Cap(string text)
    {
        return _capitalize ? text.ToUpper() : text;
    }

    // Storey order: defaults 0–6, then "Floor N" by N (N ≥ 7), anything else last.
    // Shared with MainViewModel's floor combo ordering.
    internal static int FloorOrder(string name)
    {
        return name switch
        {
            "Ground Floor" => 0,
            "First Floor" => 1,
            "Second Floor" => 2,
            "Third Floor" => 3,
            "Fourth Floor" => 4,
            "Fifth Floor" => 5,
            "Sixth Floor" => 6,
            _ => name.StartsWith("Floor ") && int.TryParse(name.AsSpan(6), out var n) ? n : 999
        };
    }
}
