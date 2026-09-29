using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FenCalc2.Models;

// Shared tolerant parsing and glazing-default helpers. The user's locale may use
// comma decimals; strings seeded with dots ("0.900") must still parse.
public static class Helpers
{
    public static double? ParseTolerant(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
    }

    // Frame category used by GlazingPerformance lookups: metal frames use the
    // Steel/Aluminium columns, everything else (timber, pvcu, ...) uses Other.
    public static bool IsSteelOrAluminium(string frame) => frame is "Steel" or "Aluminium";

    // U/SHGC default chain shared by Add Window and the Selected Window editor:
    // manufacturer-tested override on the window type wins, else the GlazingPerformance
    // row for the glass/frame combination. Missing data falls back to "0.00".
    public static (string UValue, string Shgc) ComputeUShgc(
        WindowType? type, string glass, string frame, IReadOnlyList<GlazingPerformance> glazing)
    {
        if (type?.UValue is not null && type.SHGC is not null)
            return (type.UValue.Value.ToString("F2"), type.SHGC.Value.ToString("F2"));

        if (string.IsNullOrEmpty(glass) || string.IsNullOrEmpty(frame))
            return ("0.00", "0.00");

        var row = glazing.FirstOrDefault(g => g.Glass == glass);
        if (row is null)
            return ("0.00", "0.00");

        return IsSteelOrAluminium(frame)
            ? (row.UValue_Steel.ToString("F2"), row.SHGC_Steel.ToString("F2"))
            : (row.UValue_Other.ToString("F2"), row.SHGC_Other.ToString("F2"));
    }

    // Field-for-field copy of a placement onto a (possibly different) project/floor;
    // optional plan-slot override for copies into another direction.
    public static WindowPlacement CloneWindow(WindowPlacement w, int projectId, int floorId, string? orientation = null) => new()
    {
        ProjectId = projectId,
        FloorId = floorId,
        WinType = w.WinType,
        WinWidth = w.WinWidth,
        WinHeight = w.WinHeight,
        Orientation = orientation ?? w.Orientation,
        WinGlass = w.WinGlass,
        WinFrame = w.WinFrame,
        UValue = w.UValue,
        SHGC = w.SHGC,
        P = w.P,
        G = w.G,
        RoomName = w.RoomName
    };

    public static string ImagesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FenCalc2", "Data", "Images");

    // Copies a floor-plan image to the {projectId}_{floorId}.ext naming used by the app;
    // returns the new path, or null when there is no (existing) source file.
    public static string? CopyFloorImage(string? sourcePath, int projectId, int floorId)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return null;
        Directory.CreateDirectory(ImagesDirectory);
        var dest = Path.Combine(ImagesDirectory,
            $"{projectId}_{floorId}{Path.GetExtension(sourcePath).ToLowerInvariant()}");
        File.Copy(sourcePath, dest, overwrite: true);
        return dest;
    }
}
