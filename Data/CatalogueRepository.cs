using System.Collections.Generic;
using System.Linq;
using Dapper;
using FenCalc2.Models;

namespace FenCalc2.Data;

public class CatalogueRepository
{
    private readonly System.Func<Microsoft.Data.Sqlite.SqliteConnection> _connFactory;

    public CatalogueRepository(System.Func<Microsoft.Data.Sqlite.SqliteConnection> connFactory)
    {
        _connFactory = connFactory;
    }

    // Window Ranges
    public IEnumerable<WindowRange> GetRanges()
    {
        using var conn = _connFactory();
        return conn.Query<WindowRange>("SELECT * FROM WindowRanges ORDER BY WinRange");
    }

    public void InsertRange(string range)
    {
        using var conn = _connFactory();
        conn.Execute("INSERT OR IGNORE INTO WindowRanges (WinRange) VALUES (@Range)", new { Range = range });
    }

    public void DeleteRange(string range)
    {
        using var conn = _connFactory();
        conn.Execute("DELETE FROM WindowRanges WHERE WinRange = @Range", new { Range = range });
    }

    // Window Types
    public IEnumerable<WindowType> GetTypesByRange(string range)
    {
        using var conn = _connFactory();
        return conn.Query<WindowType>(
            "SELECT Id, WinRange, WinType AS Name, WinWidth, WinHeight, WinGlass, WinFrame, UValue, SHGC FROM WindowTypes WHERE WinRange = @Range ORDER BY WinType",
            new { Range = range });
    }

    public IEnumerable<WindowType> GetAllTypes()
    {
        using var conn = _connFactory();
        return conn.Query<WindowType>("SELECT Id, WinRange, WinType AS Name, WinWidth, WinHeight, WinGlass, WinFrame, UValue, SHGC FROM WindowTypes ORDER BY WinRange, WinType");
    }

    public WindowType? GetTypeByName(string range, string winType)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<WindowType>(
            "SELECT Id, WinRange, WinType AS Name, WinWidth, WinHeight, WinGlass, WinFrame, UValue, SHGC FROM WindowTypes WHERE WinRange = @Range AND WinType = @Type",
            new { Range = range, Type = winType });
    }

    public void InsertType(WindowType wt)
    {
        using var conn = _connFactory();
        conn.Execute("""
            INSERT INTO WindowTypes (WinRange, WinType, WinWidth, WinHeight, WinGlass, WinFrame, UValue, SHGC)
            VALUES (@WinRange, @Name, @WinWidth, @WinHeight, @WinGlass, @WinFrame, @UValue, @SHGC)
            """, wt);
    }

    public void DeleteType(string range, string winType)
    {
        using var conn = _connFactory();
        conn.Execute(
            "DELETE FROM WindowTypes WHERE WinRange = @Range AND WinType = @Type",
            new { Range = range, Type = winType });
    }

    // Glazing Performance
    public IEnumerable<GlazingPerformance> GetAllGlazing()
    {
        using var conn = _connFactory();
        return conn.Query<GlazingPerformance>("SELECT * FROM GlazingPerformance ORDER BY Glass");
    }

    public GlazingPerformance? GetGlazing(string glass)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<GlazingPerformance>(
            "SELECT * FROM GlazingPerformance WHERE Glass = @Glass", new { Glass = glass });
    }

    public void InsertGlazing(GlazingPerformance gp)
    {
        using var conn = _connFactory();
        conn.Execute("""
            INSERT OR REPLACE INTO GlazingPerformance (Glass, UValue_Steel, SHGC_Steel, UValue_Other, SHGC_Other)
            VALUES (@Glass, @UValue_Steel, @SHGC_Steel, @UValue_Other, @SHGC_Other)
            """, gp);
    }

    // Window Frames
    public IEnumerable<WindowFrame> GetAllFrames()
    {
        using var conn = _connFactory();
        return conn.Query<WindowFrame>("SELECT * FROM WindowFrames ORDER BY FrameType");
    }

    public void InsertFrame(string frameType)
    {
        using var conn = _connFactory();
        conn.Execute("INSERT OR IGNORE INTO WindowFrames (FrameType) VALUES (@Type)", new { Type = frameType });
    }

    // Climate Zones
    public IEnumerable<ClimateZoneRow> GetClimateZone(int zone)
    {
        using var conn = _connFactory();
        return conn.Query<ClimateZoneRow>(
            "SELECT * FROM ClimateZones WHERE Zone = @Zone ORDER BY PH",
            new { Zone = zone });
    }

    public ClimateZoneRow? GetClimateZoneRow(int zone, string ph)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<ClimateZoneRow>(
            "SELECT * FROM ClimateZones WHERE Zone = @Zone AND PH = @PH",
            new { Zone = zone, PH = ph });
    }

    public void InsertClimateZoneRow(ClimateZoneRow row)
    {
        using var conn = _connFactory();
        conn.Execute("""
            INSERT OR REPLACE INTO ClimateZones (Zone, PH, North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest)
            VALUES (@Zone, @PH, @North, @NorthEast, @East, @SouthEast, @South, @SouthWest, @West, @NorthWest)
            """, row);
    }
}
