using System.Collections.Generic;
using System.Linq;
using Dapper;
using FenCalc2.Models;

namespace FenCalc2.Data;

public class WindowPlacementRepository
{
    private readonly System.Func<Microsoft.Data.Sqlite.SqliteConnection> _connFactory;

    public WindowPlacementRepository(System.Func<Microsoft.Data.Sqlite.SqliteConnection> connFactory)
    {
        _connFactory = connFactory;
    }

    public IEnumerable<WindowPlacement> GetByProject(int projectId)
    {
        using var conn = _connFactory();
        return conn.Query<WindowPlacement>(
            "SELECT * FROM WindowPlacements WHERE ProjectId = @ProjectId ORDER BY Id",
            new { ProjectId = projectId });
    }

    public IEnumerable<WindowPlacement> GetByFloor(int floorId)
    {
        using var conn = _connFactory();
        return conn.Query<WindowPlacement>(
            "SELECT * FROM WindowPlacements WHERE FloorId = @FloorId ORDER BY Id",
            new { FloorId = floorId });
    }

    public IEnumerable<WindowPlacement> GetByFloorAndOrientation(int floorId, string orientation)
    {
        using var conn = _connFactory();
        return conn.Query<WindowPlacement>(
            "SELECT * FROM WindowPlacements WHERE FloorId = @FloorId AND Orientation = @Orientation ORDER BY Id",
            new { FloorId = floorId, Orientation = orientation });
    }

    public WindowPlacement? GetById(int id)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<WindowPlacement>(
            "SELECT * FROM WindowPlacements WHERE Id = @Id", new { Id = id });
    }

    public int Insert(WindowPlacement wp)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<int>("""
            INSERT INTO WindowPlacements (ProjectId, FloorId, WinType, WinWidth, WinHeight, Orientation, WinGlass, WinFrame, UValue, SHGC, P, G, RoomName)
            VALUES (@ProjectId, @FloorId, @WinType, @WinWidth, @WinHeight, @Orientation, @WinGlass, @WinFrame, @UValue, @SHGC, @P, @G, @RoomName);
            SELECT last_insert_rowid();
            """, wp);
    }

    public void Update(WindowPlacement wp)
    {
        using var conn = _connFactory();
        conn.Execute("""
            UPDATE WindowPlacements
            SET FloorId = @FloorId, WinType = @WinType, WinWidth = @WinWidth, WinHeight = @WinHeight,
                Orientation = @Orientation, WinGlass = @WinGlass, WinFrame = @WinFrame,
                UValue = @UValue, SHGC = @SHGC, P = @P, G = @G, RoomName = @RoomName
            WHERE Id = @Id
            """, wp);
    }

    public void UpdateOrientation(int id, string orientation)
    {
        using var conn = _connFactory();
        conn.Execute(
            "UPDATE WindowPlacements SET Orientation = @Orientation WHERE Id = @Id",
            new { Id = id, Orientation = orientation });
    }

    // Mirrors all plan slots of a project: swaps left/right columns (N/S unchanged)
    public void MirrorOrientations(int projectId)
    {
        using var conn = _connFactory();
        conn.Execute("""
            UPDATE WindowPlacements
            SET Orientation = CASE Orientation
                WHEN 'North East' THEN 'North West'
                WHEN 'North West' THEN 'North East'
                WHEN 'East' THEN 'West'
                WHEN 'West' THEN 'East'
                WHEN 'South East' THEN 'South West'
                WHEN 'South West' THEN 'South East'
                ELSE Orientation
            END
            WHERE ProjectId = @ProjectId
            """, new { ProjectId = projectId });
    }

    public void Delete(int id)
    {
        using var conn = _connFactory();
        conn.Execute("DELETE FROM WindowPlacements WHERE Id = @Id", new { Id = id });
    }

    public int CountByFloor(int floorId)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WindowPlacements WHERE FloorId = @FloorId",
            new { FloorId = floorId });
    }

    public int CountByFloorAndType(int floorId, string winType)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WindowPlacements WHERE FloorId = @FloorId AND WinType = @WinType",
            new { FloorId = floorId, WinType = winType });
    }

    public IEnumerable<string> GetDistinctTypesByFloor(int floorId)
    {
        using var conn = _connFactory();
        return conn.Query<string>(
            "SELECT DISTINCT WinType FROM WindowPlacements WHERE FloorId = @FloorId ORDER BY WinType",
            new { FloorId = floorId });
    }

    public IEnumerable<WindowPlacement> GetByProjectAndFloorName(int projectId, string floorName)
    {
        using var conn = _connFactory();
        return conn.Query<WindowPlacement>(@"
            SELECT wp.* FROM WindowPlacements wp
            INNER JOIN Floors f ON wp.FloorId = f.Id
            WHERE wp.ProjectId = @ProjectId AND f.FloorName = @FloorName
            ORDER BY wp.Id",
            new { ProjectId = projectId, FloorName = floorName });
    }
}
