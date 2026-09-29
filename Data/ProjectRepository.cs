using System.Collections.Generic;
using System.Linq;
using Dapper;
using FenCalc2.Models;

namespace FenCalc2.Data;

public class ProjectRepository
{
    private readonly System.Func<Microsoft.Data.Sqlite.SqliteConnection> _connFactory;

    public ProjectRepository(System.Func<Microsoft.Data.Sqlite.SqliteConnection> connFactory)
    {
        _connFactory = connFactory;
    }

    public IEnumerable<FenCalcProject> GetAll()
    {
        using var conn = _connFactory();
        return conn.Query<FenCalcProject>("SELECT * FROM Projects ORDER BY ClientName, ProjectName, BuildingName");
    }

    public IEnumerable<FenCalcProject> GetByClient(string client)
    {
        using var conn = _connFactory();
        return conn.Query<FenCalcProject>(
            "SELECT * FROM Projects WHERE ClientName = @Client ORDER BY ProjectName, BuildingName",
            new { Client = client });
    }

    public IEnumerable<FenCalcProject> GetByClientAndProject(string client, string project)
    {
        using var conn = _connFactory();
        return conn.Query<FenCalcProject>(
            "SELECT * FROM Projects WHERE ClientName = @Client AND ProjectName = @Project ORDER BY BuildingName",
            new { Client = client, Project = project });
    }

    public FenCalcProject? GetById(int id)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<FenCalcProject>(
            "SELECT * FROM Projects WHERE Id = @Id", new { Id = id });
    }

    public bool Exists(string buildingName, string clientName, string projectName)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Projects WHERE BuildingName = @B AND ClientName = @C AND ProjectName = @P",
            new { B = buildingName, C = clientName, P = projectName }) > 0;
    }

    public int Insert(FenCalcProject project)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<int>("""
            INSERT INTO Projects (BuildingName, ClientName, ProjectName, ClimateZone, Orientation)
            VALUES (@BuildingName, @ClientName, @ProjectName, @ClimateZone, @Orientation);
            SELECT last_insert_rowid();
            """, project);
    }

    public void Update(FenCalcProject project)
    {
        using var conn = _connFactory();
        conn.Execute("""
            UPDATE Projects
            SET BuildingName = @BuildingName, ClientName = @ClientName, ProjectName = @ProjectName,
                ClimateZone = @ClimateZone, Orientation = @Orientation,
                RotationOffset = @RotationOffset, IsMirrored = @IsMirrored
            WHERE Id = @Id
            """, project);
    }

    public void UpdateCompassState(int id, int rotationOffset, bool isMirrored)
    {
        using var conn = _connFactory();
        conn.Execute(
            "UPDATE Projects SET RotationOffset = @RotationOffset, IsMirrored = @IsMirrored WHERE Id = @Id",
            new { Id = id, RotationOffset = rotationOffset, IsMirrored = isMirrored });
    }

    public void Delete(int id)
    {
        using var conn = _connFactory();
        conn.Execute("DELETE FROM Projects WHERE Id = @Id", new { Id = id });
    }

    // AppSettings (single key/value store, e.g. last-opened project)
    public string? GetSetting(string key)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<string?>(
            "SELECT Value FROM AppSettings WHERE Key = @Key", new { Key = key });
    }

    public void SetSetting(string key, string value)
    {
        using var conn = _connFactory();
        conn.Execute(
            "INSERT OR REPLACE INTO AppSettings (Key, Value) VALUES (@Key, @Value)",
            new { Key = key, Value = value });
    }

    public IEnumerable<string> GetDistinctClients()
    {
        using var conn = _connFactory();
        return conn.Query<string>("SELECT DISTINCT ClientName FROM Projects ORDER BY ClientName");
    }

    public IEnumerable<string> GetDistinctProjects(string client)
    {
        using var conn = _connFactory();
        return conn.Query<string>(
            "SELECT DISTINCT ProjectName FROM Projects WHERE ClientName = @Client ORDER BY ProjectName",
            new { Client = client });
    }

    public IEnumerable<string> GetDistinctBuildings(string client, string project)
    {
        using var conn = _connFactory();
        return conn.Query<string>(
            "SELECT DISTINCT BuildingName FROM Projects WHERE ClientName = @Client AND ProjectName = @Project ORDER BY BuildingName",
            new { Client = client, Project = project });
    }
}
