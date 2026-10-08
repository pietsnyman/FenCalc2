using System.Collections.Generic;
using System.Linq;
using Dapper;
using FenCalc2.Models;

namespace FenCalc2.Data;

public class WallRepository
{
    private readonly System.Func<Microsoft.Data.Sqlite.SqliteConnection> _connFactory;

    public WallRepository(System.Func<Microsoft.Data.Sqlite.SqliteConnection> connFactory)
    {
        _connFactory = connFactory;
    }

    // ---- assemblies -------------------------------------------------------------

    public IEnumerable<WallAssembly> GetByProject(int projectId)
    {
        using var conn = _connFactory();
        return conn.Query<WallAssembly>(
            "SELECT * FROM WallAssemblies WHERE ProjectId = @ProjectId ORDER BY Id",
            new { ProjectId = projectId });
    }

    public WallAssembly? GetById(int id)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<WallAssembly>(
            "SELECT * FROM WallAssemblies WHERE Id = @Id", new { Id = id });
    }

    public int Insert(WallAssembly wall)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<int>("""
            INSERT INTO WallAssemblies (ProjectId, Name, Category1, AttachmentMode)
            VALUES (@ProjectId, @Name, @Category1, @AttachmentMode);
            SELECT last_insert_rowid();
            """, wall);
    }

    public void Update(WallAssembly wall)
    {
        using var conn = _connFactory();
        conn.Execute("""
            UPDATE WallAssemblies
            SET Name = @Name, Category1 = @Category1, AttachmentMode = @AttachmentMode
            WHERE Id = @Id
            """, wall);
    }

    public void Delete(int id)
    {
        using var conn = _connFactory();
        conn.Execute("DELETE FROM WallAssemblies WHERE Id = @Id", new { Id = id });
    }

    // ---- layers -----------------------------------------------------------------

    private const string LayerSelect = """
        SELECT w.*, m.Lambda AS LibraryLambda, m.Density AS LibraryDensity,
               m.CSpec AS LibraryC, m.FixedR, m.PlasterFree, m.Presets,
               m.Name AS MaterialName
        FROM WallLayers w
        LEFT JOIN WallMaterials m ON w.MaterialId = m.Id
        """;

    public IEnumerable<WallLayer> GetLayers(int wallId)
    {
        using var conn = _connFactory();
        return conn.Query<WallLayer>(LayerSelect + " WHERE w.WallId = @WallId ORDER BY w.Pos",
            new { WallId = wallId });
    }

    public int InsertLayer(WallLayer layer)
    {
        using var conn = _connFactory();
        return conn.ExecuteScalar<int>("""
            INSERT INTO WallLayers (WallId, Pos, MaterialId, Name, Category, ThicknessMm,
                                    LambdaOverride, DensityOverride, COverride)
            VALUES (@WallId, @Pos, @MaterialId, @Name, @Category, @ThicknessMm,
                    @LambdaOverride, @DensityOverride, @COverride);
            SELECT last_insert_rowid();
            """, layer);
    }

    public void UpdateLayer(WallLayer layer)
    {
        using var conn = _connFactory();
        conn.Execute("""
            UPDATE WallLayers
            SET Pos = @Pos, MaterialId = @MaterialId, Name = @Name, Category = @Category,
                ThicknessMm = @ThicknessMm, LambdaOverride = @LambdaOverride,
                DensityOverride = @DensityOverride, COverride = @COverride
            WHERE Id = @Id
            """, layer);
    }

    public void DeleteLayer(int id)
    {
        using var conn = _connFactory();
        conn.Execute("DELETE FROM WallLayers WHERE Id = @Id", new { Id = id });
    }

    /// <summary>Persist a full reordering: layerIds[i] gets position i. Reordering in
    /// one transaction keeps drag/drop and the ▲▼ buttons atomic.</summary>
    public void ReplaceOrder(IEnumerable<int> layerIds)
    {
        using var conn = _connFactory();
        conn.Open();
        using var tx = conn.BeginTransaction();
        var pos = 0;
        foreach (var id in layerIds)
            conn.Execute("UPDATE WallLayers SET Pos = @Pos WHERE Id = @Id",
                new { Pos = pos++, Id = id }, tx);
        tx.Commit();
    }

    // ---- reference data (seeded by Xa2026Seeder) --------------------------------

    public IEnumerable<WallMaterial> GetMaterials()
    {
        using var conn = _connFactory();
        return conn.Query<WallMaterial>("SELECT * FROM WallMaterials ORDER BY Id");
    }

    public WallZoneRequirement? GetZoneRequirement(string energyZone)
    {
        using var conn = _connFactory();
        return conn.QueryFirstOrDefault<WallZoneRequirement>(
            "SELECT * FROM WallZoneRequirements WHERE EnergyZone = @Zone",
            new { Zone = energyZone });
    }

    /// <summary>Energy zones of the wall requirements table, sorted1..7 with 5H between
    ///5 and6, in the same display form as the climate combo ("Zone 1"…"Zone 5H").</summary>
    public IEnumerable<string> GetZones()
    {
        using var conn = _connFactory();
        return conn.Query<string>("""
            SELECT DISTINCT 'Zone ' || EnergyZone FROM WallZoneRequirements
            ORDER BY CASE EnergyZone WHEN '5H' THEN 5.5 ELSE CAST(EnergyZone AS REAL) END
            """);
    }
}
