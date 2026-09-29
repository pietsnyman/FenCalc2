using System;
using System.Collections.Generic;
using System.Linq;
using FenCalc2.Data;
using FenCalc2.Models;

namespace FenCalc2.Services;

public class SolarCalculator
{
    private readonly CatalogueRepository _catalogueRepo;
    private List<ClimateZoneRow>? _cachedZoneData;
    private int _cachedZone = -1;

    public SolarCalculator(CatalogueRepository catalogueRepo)
    {
        _catalogueRepo = catalogueRepo;
    }

    // Climate zone constants
    private static readonly Dictionary<int, (double Conductance, double Shg)> ZoneConstants = new()
    {
        { 1, (1.2, 0.15) },
        { 2, (1.4, 0.12) },
        { 3, (1.4, 0.10) },
        { 4, (1.4, 0.13) },
        { 5, (1.4, 0.11) },
        { 6, (1.2, 0.13) },
    };

    public static int GetZoneNumber(string zoneName)
    {
        if (int.TryParse(zoneName.Replace("Zone ", ""), out var n))
            return n;
        return 1;
    }

    public static (double Conductance, double Shg) GetZoneConstants(string zoneName)
    {
        var n = GetZoneNumber(zoneName);
        return ZoneConstants.TryGetValue(n, out var c) ? c : (1.2, 0.15);
    }

    /// <summary>
    /// Snaps a continuous P/H ratio to the nearest 0.05 bucket.
    /// </summary>
    public static double SnapPHBucket(double ph)
    {
        if (ph < 0.05) return 0.00;
        if (ph >= 2.00) return 2.00;
        return Math.Floor(ph / 0.05) * 0.05;
    }

    /// <summary>
    /// Gets the solar exposure factor for a given P/H ratio, climate zone, and orientation.
    /// Returns the factor (already divided by 100 from DB).
    /// </summary>
    public double GetSolarExposureFactor(double ph, int zoneNumber, string orientation)
    {
        if (_cachedZone != zoneNumber || _cachedZoneData is null)
        {
            _cachedZoneData = _catalogueRepo.GetClimateZone(zoneNumber).ToList();
            _cachedZone = zoneNumber;
        }

        var bucket = SnapPHBucket(ph);
        // Use comma separator for DB lookup (South African locale)
        var phStr = bucket.ToString("0.00").Replace(".", ",");

        var row = _cachedZoneData.FirstOrDefault(r => r.PH == phStr);
        if (row is null) return 0;

        var columnValue = orientation switch
        {
            "North" => row.North,
            "North East" => row.NorthEast,
            "East" => row.East,
            "South East" => row.SouthEast,
            "South" => row.South,
            "South West" => row.SouthWest,
            "West" => row.West,
            "North West" => row.NorthWest,
            _ => 0
        };

        return columnValue / 100.0;
    }

    /// <summary>
    /// Calculates P/H ratio for a window.
    /// </summary>
    public static double CalculatePH(int p, int g, double heightM)
    {
        double winH = g + (heightM * 1000); // mm
        if (winH <= 0) return 0;

        if (g > 500)
            return p * 0.5 / winH;
        else
            return p / winH;
    }

    /// <summary>
    /// Calculates solar heat gain for a single window.
    /// </summary>
    public double CalculateWindowSHG(WindowPlacement wp, int zoneNumber)
    {
        var ph = CalculatePH(wp.P, wp.G, wp.WinHeight);
        var factor = GetSolarExposureFactor(ph, zoneNumber, wp.Orientation);
        return wp.WinWidth * wp.WinHeight * wp.SHGC * factor;
    }

    /// <summary>
    /// Calculates conductance for a set of windows.
    /// </summary>
    public static double CalculateConductance(int count, double area, double uValue)
    {
        return count * area * uValue;
    }

    /// <summary>
    /// Calculates window area.
    /// </summary>
    public static double CalculateWindowArea(double width, double height)
    {
        return width * height;
    }

    /// <summary>
    /// Calculates floor area ratio as a percentage.
    /// </summary>
    public static double CalculateFloorAreaRatio(double totalWindowArea, double floorArea)
    {
        if (floorArea <= 0) return 0;
        return totalWindowArea / floorArea * 100;
    }
}
