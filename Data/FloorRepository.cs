using System.Collections.Generic;
using System.Linq;
using Dapper;
using FenCalc2.Models;

namespace FenCalc2.Data;

public class FloorRepository
{
    private readonly System.Func<Microsoft.Data.Sqlite.SqliteConnection> _connFactory;

    public FloorRepository(System.Func<Microsoft.Data.Sqlite.SqliteConnection> connFactory)
    {
        _connFactory = connFactory;
    }

    public IEnumerable<Floor> GetByProject(int projectId)
    {
        using var conn = _connFactory();
        return conn.Query<Floor>(
            "SELECT * FROM Floors WHERE ProjectId = @ProjectId ORDER BY Id",
            new { ProjectId = projectId });
    }

    public Floor? GetById(int id)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<Floor>(
            "SELECT * FROM Floors WHERE Id = @Id", new { Id = id });
    }

    public Floor? GetByProjectAndName(int projectId, string floorName)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<Floor>(
            "SELECT * FROM Floors WHERE ProjectId = @ProjectId AND FloorName = @FloorName",
            new { ProjectId = projectId, FloorName = floorName });
    }

    public int Insert(Floor floor)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<int>("""
            INSERT INTO Floors (ProjectId, FloorName, FloorArea, ImagePath)
            VALUES (@ProjectId, @FloorName, @FloorArea, @ImagePath);
            SELECT last_insert_rowid();
            """, floor);
    }

    public void Update(Floor floor)
    {
        using var conn = _connFactory();
        conn.Execute("""
            UPDATE Floors
            SET FloorName = @FloorName, FloorArea = @FloorArea, ImagePath = @ImagePath
            WHERE Id = @Id
            """, floor);
    }

    public void UpdateArea(int id, double? area)
    {
        using var conn = _connFactory();
        conn.Execute("UPDATE Floors SET FloorArea = @Area WHERE Id = @Id", new { Id = id, Area = area });
    }

    public void UpdateImagePath(int id, string? path)
    {
        using var conn = _connFactory();
        conn.Execute("UPDATE Floors SET ImagePath = @Path WHERE Id = @Id", new { Id = id, Path = path });
    }

    public void Delete(int id)
    {
        using var conn = _connFactory();
        conn.Execute("DELETE FROM Floors WHERE Id = @Id", new { Id = id });
    }
}
