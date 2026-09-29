using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using Dapper;
using FenCalc2.Models;
using Microsoft.Data.Sqlite;

namespace FenCalc2.Data;

public static class DataMigrator
{
    private static readonly string[] CompassNames =
        { "North", "North East", "East", "South East", "South", "South West", "West", "North West" };

    private const string OldDbPassword = "59s93kfEHRY1gae1432645fdADFGwqa4yjLLS";

    public static string GetOldDbPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "FenCalc", "Data", "FenCalcDB.db");
    }

    public static bool OldDbExists()
    {
        return File.Exists(GetOldDbPath());
    }

    public static MigrationResult Migrate(string? oldDbPath = null)
    {
        oldDbPath ??= GetOldDbPath();
        if (!File.Exists(oldDbPath))
            return new MigrationResult { Success = false, Error = "Old database not found at: " + oldDbPath };

        var result = new MigrationResult();

        try
        {
            // Open old DB (read-only)
            var oldConnStr = $"Data Source={oldDbPath};Version=3;Password={OldDbPassword};Read Only=true;";
            using var oldConn = new SQLiteConnection(oldConnStr);
            oldConn.Open();

            // Open new DB
            using var newConn = Database.CreateConnection();
            Migrations.Run(newConn);

            // Migrate lookup tables first
            MigrateGlazingPerformance(oldConn, newConn, result);
            MigrateWindowFrames(oldConn, newConn, result);
            MigrateWindowRanges(oldConn, newConn, result);
            MigrateWindowTypes(oldConn, newConn, result);
            MigrateClimateZones(oldConn, newConn, result);

            // Migrate projects and windows
            MigrateProjects(oldConn, newConn, result);

            // Legacy floor-plan images: always runs (idempotent), even when the
            // project import guard skips a redundant run.
            ImportLegacyImages(oldConn, newConn, result, oldDbPath);

            if (result.Error is null)
                result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    private static void MigrateGlazingPerformance(SQLiteConnection oldConn, SqliteConnection newConn, MigrationResult result)
    {
        using var cmd = new SQLiteCommand("SELECT * FROM GlazingPerformance", oldConn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            newConn.Execute("""
                INSERT OR REPLACE INTO GlazingPerformance (Glass, UValue_Steel, SHGC_Steel, UValue_Other, SHGC_Other)
                VALUES (@Glass, @UValue_Steel, @SHGC_Steel, @UValue_Other, @SHGC_Other)
                """, new
            {
                Glass = rdr.GetString(0),
                UValue_Steel = rdr.GetDouble(1),
                SHGC_Steel = rdr.GetDouble(2),
                UValue_Other = rdr.GetDouble(3),
                SHGC_Other = rdr.GetDouble(4)
            });
            result.GlazingCount++;
        }
    }

    private static void MigrateWindowFrames(SQLiteConnection oldConn, SqliteConnection newConn, MigrationResult result)
    {
        using var cmd = new SQLiteCommand("SELECT * FROM WindowFrames", oldConn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            newConn.Execute("INSERT OR IGNORE INTO WindowFrames (FrameType) VALUES (@Type)",
                new { Type = rdr.GetString(0) });
            result.FrameCount++;
        }
    }

    private static void MigrateWindowRanges(SQLiteConnection oldConn, SqliteConnection newConn, MigrationResult result)
    {
        using var cmd = new SQLiteCommand("SELECT * FROM WindowRanges", oldConn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            newConn.Execute("INSERT OR IGNORE INTO WindowRanges (WinRange) VALUES (@Range)",
                new { Range = rdr.GetString(0) });
            result.RangeCount++;
        }
    }

    private static void MigrateWindowTypes(SQLiteConnection oldConn, SqliteConnection newConn, MigrationResult result)
    {
        using var cmd = new SQLiteCommand("SELECT * FROM Windows", oldConn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            newConn.Execute("""
                INSERT OR IGNORE INTO WindowTypes (WinRange, WinType, WinWidth, WinHeight, WinGlass, WinFrame)
                VALUES (@WinRange, @WinType, @WinWidth, @WinHeight, @WinGlass, @WinFrame)
                """, new
            {
                WinRange = rdr.GetString(5),
                WinType = rdr.GetString(0),
                WinWidth = rdr.IsDBNull(1) ? (double?)null : rdr.GetDouble(1),
                WinHeight = rdr.IsDBNull(2) ? (double?)null : rdr.GetDouble(2),
                WinGlass = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                WinFrame = rdr.IsDBNull(4) ? null : rdr.GetString(4)
            });
            result.TypeCount++;
        }
    }

    private static void MigrateClimateZones(SQLiteConnection oldConn, SqliteConnection newConn, MigrationResult result)
    {
        for (int zone = 1; zone <= 6; zone++)
        {
            var tableName = $"ClimateZone{zone}";
            using var cmd = new SQLiteCommand($"SELECT * FROM [{tableName}]", oldConn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                newConn.Execute("""
                    INSERT OR REPLACE INTO ClimateZones (Zone, PH, North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest)
                    VALUES (@Zone, @PH, @North, @NorthEast, @East, @SouthEast, @South, @SouthWest, @West, @NorthWest)
                    """, new
                {
                    Zone = zone,
                    PH = rdr.GetString(0),
                    North = rdr.GetDouble(1),
                    NorthEast = rdr.GetDouble(2),
                    East = rdr.GetDouble(3),
                    SouthEast = rdr.GetDouble(4),
                    South = rdr.GetDouble(5),
                    SouthWest = rdr.GetDouble(6),
                    West = rdr.GetDouble(7),
                    NorthWest = rdr.GetDouble(8)
                });
                result.ClimateZoneRowCount++;
            }
        }
    }

    // The legacy DB stores almost everything as TEXT (nvarchar) with comma decimal
    // separators (e.g. "51,35"), and empty strings instead of NULL — so raw typed
    // accessors like GetDouble() throw "Specified cast is not valid". All conversions
    // below are tolerant: numeric text with ',' or '.', real numeric cell types,
    // blank -> null/default.
    private static string Str(object? v) =>
        v is null or DBNull ? string.Empty : (Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty);

    private static double? Num(object? v)
    {
        if (v is null or DBNull) return null;
        if (v is double d) return d;
        if (v is float f) return f;
        if (v is long l) return l;
        if (v is int i) return i;
        var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int NumInt(object? v) =>
        Num(v) is { } d ? (int)Math.Round(d) : 0;

    private static string NormalizeFloor(string floorName)
    {
        var t = floorName.Trim();
        return string.Equals(t, "Ground", StringComparison.OrdinalIgnoreCase) ? "Ground Floor" : t;
    }

    private static void MigrateProjects(SQLiteConnection oldConn, SqliteConnection newConn, MigrationResult result)
    {
        // Guard against duplicate imports: projects/windows are inserted without
        // any dedup key, so a second run would duplicate everything.
        if (newConn.ExecuteScalar<int>("SELECT COUNT(*) FROM Projects") > 0)
        {
            result.ProjectsExisted = true;
            return;
        }

        // Read all unique projects from old ProjectName table
        var oldProjects = new List<(string ProjName, string Client, string Project, string ClimateZone, string Orientation,
            double? FloorArea, double? FloorArea1, double? FloorArea2, double? FloorArea3, double? FloorArea4, double? FloorArea5, double? FloorArea6)>();

        using (var cmd = new SQLiteCommand("SELECT * FROM ProjectName", oldConn))
        using (var rdr = cmd.ExecuteReader())
        {
            while (rdr.Read())
            {
                oldProjects.Add((
                    Str(rdr.GetValue(0)),
                    Str(rdr.GetValue(3)),
                    Str(rdr.GetValue(4)),
                    StrOr(rdr.GetValue(6), "Zone 1"),
                    StrOr(rdr.GetValue(2), "North"),
                    Num(rdr.GetValue(1)),
                    Num(rdr.GetValue(7)),
                    Num(rdr.GetValue(8)),
                    Num(rdr.GetValue(9)),
                    Num(rdr.GetValue(10)),
                    Num(rdr.GetValue(11)),
                    Num(rdr.GetValue(12))
                ));
            }
        }

        // Create a mapping from old project key to new project ID + its north anchor index
        var projectMap = new Dictionary<string, (int Id, int AnchorIdx)>();

        foreach (var p in oldProjects)
        {
            var projectKey = $"{p.ProjName}|{p.Client}|{p.Project}";
            if (projectMap.ContainsKey(projectKey)) continue;

            var projectId = newConn.ExecuteScalar<int>("""
                INSERT INTO Projects (BuildingName, ClientName, ProjectName, ClimateZone, Orientation)
                VALUES (@BuildingName, @ClientName, @ProjectName, @ClimateZone, @Orientation);
                SELECT last_insert_rowid();
                """, new
            {
                BuildingName = p.ProjName,
                ClientName = p.Client,
                ProjectName = p.Project,
                ClimateZone = p.ClimateZone,
                Orientation = p.Orientation
            });

            var anchorIdx = Array.IndexOf(CompassNames, p.Orientation);
            if (anchorIdx < 0) anchorIdx = 0;
            projectMap[projectKey] = (projectId, anchorIdx);

            // Create floors
            var floorAreas = new[] { p.FloorArea, p.FloorArea1, p.FloorArea2, p.FloorArea3, p.FloorArea4, p.FloorArea5, p.FloorArea6 };
            var floorNames = new[] { "Ground Floor", "First Floor", "Second Floor", "Third Floor", "Fourth Floor", "Fifth Floor", "Sixth Floor" };

            for (int i = 0; i < floorNames.Length; i++)
            {
                if (i == 0 || floorAreas[i].HasValue)
                {
                    newConn.Execute("""
                        INSERT INTO Floors (ProjectId, FloorName, FloorArea)
                        VALUES (@ProjectId, @FloorName, @FloorArea)
                        """, new { ProjectId = projectId, FloorName = floorNames[i], FloorArea = floorAreas[i] });
                }
            }

            result.ProjectCount++;
        }

        // Migrate window placements from old Projects table
        using (var cmd = new SQLiteCommand("SELECT * FROM Projects", oldConn))
        using (var rdr = cmd.ExecuteReader())
        {
            while (rdr.Read())
            {
                var projName = Str(rdr.GetValue(12));
                var client = Str(rdr.GetValue(14));
                var project = Str(rdr.GetValue(15));
                var floor = NormalizeFloor(Str(rdr.GetValue(16)));
                var projectKey = $"{projName}|{client}|{project}";

                if (!projectMap.TryGetValue(projectKey, out var projectInfo))
                    continue;
                var projectId = projectInfo.Id;

                // Orientation in the legacy DB is a true compass direction; the new schema
                // stores the image-relative plan slot (mirroring Migration v3 semantics)
                var trueOri = StrOr(rdr.GetValue(3), "North");
                var trueIdx = Array.IndexOf(CompassNames, trueOri);
                if (trueIdx < 0) trueIdx = 0;
                var planSlot = CompassNames[(trueIdx - projectInfo.AnchorIdx + 8) % 8];

                // Find the floor ID
                var floorId = newConn.ExecuteScalar<int>(
                    "SELECT Id FROM Floors WHERE ProjectId = @ProjectId AND FloorName = @FloorName",
                    new { ProjectId = projectId, FloorName = floor });

                if (floorId == 0) continue;

                var roomName = Str(rdr.GetValue(11));

                newConn.Execute("""
                    INSERT INTO WindowPlacements (ProjectId, FloorId, WinType, WinWidth, WinHeight, Orientation, WinGlass, WinFrame, UValue, SHGC, P, G, RoomName)
                    VALUES (@ProjectId, @FloorId, @WinType, @WinWidth, @WinHeight, @Orientation, @WinGlass, @WinFrame, @UValue, @SHGC, @P, @G, @RoomName)
                    """, new
                {
                    ProjectId = projectId,
                    FloorId = floorId,
                    WinType = Str(rdr.GetValue(0)),
                    WinWidth = Num(rdr.GetValue(1)) ?? 0.0,
                    WinHeight = Num(rdr.GetValue(2)) ?? 0.0,
                    Orientation = planSlot,
                    WinGlass = Str(rdr.GetValue(4)),
                    WinFrame = Str(rdr.GetValue(5)),
                    UValue = Num(rdr.GetValue(6)) ?? 0.0,
                    SHGC = Num(rdr.GetValue(7)) ?? 0.0,
                    P = NumInt(rdr.GetValue(8)) == 0 ? 375 : NumInt(rdr.GetValue(8)),
                    G = NumInt(rdr.GetValue(9)) == 0 ? 160 : NumInt(rdr.GetValue(9)),
                    RoomName = string.IsNullOrWhiteSpace(roomName) ? null : roomName
                });

                result.WindowCount++;
            }
        }
    }

    private static string StrOr(object? v, string fallback)
    {
        var s = Str(v);
        return string.IsNullOrWhiteSpace(s) ? fallback : s;
    }

    // Legacy DB stores per-floor image paths (ProjectName: Image, Image1..Image6; the
    // paths may reference old machine folders, so files are resolved by basename in the
    // legacy images folder). Sources are scanned read-only; files are COPIED into the
    // app's managed images folder. Idempotent: floors that already have an image (or a
    // copy already made) are never touched.
    private static void ImportLegacyImages(SQLiteConnection oldConn, SqliteConnection newConn, MigrationResult result, string oldDbPath)
    {
        var imagesRoot = Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(oldDbPath)) ?? string.Empty, "Images");
        if (!Directory.Exists(imagesRoot))
            return;

        var newImagesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FenCalc2", "Data", "Images");
        Directory.CreateDirectory(newImagesDir);

        // Ground = col 5, First..Sixth = cols 13..18
        var imageColumns = new[] { 5, 13, 14, 15, 16, 17, 18 };
        var floorNames = new[] { "Ground Floor", "First Floor", "Second Floor", "Third Floor", "Fourth Floor", "Fifth Floor", "Sixth Floor" };

        using var cmd = new SQLiteCommand("SELECT * FROM ProjectName", oldConn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var buildingName = Str(rdr.GetValue(0));
            var clientName = Str(rdr.GetValue(3));
            var projectName = Str(rdr.GetValue(4));
            if (string.IsNullOrWhiteSpace(buildingName) ||
                string.IsNullOrWhiteSpace(clientName) ||
                string.IsNullOrWhiteSpace(projectName))
                continue;

            var projectId = newConn.ExecuteScalar<int>(
                "SELECT Id FROM Projects WHERE BuildingName = @B AND ClientName = @C AND ProjectName = @P",
                new { B = buildingName, C = clientName, P = projectName });
            if (projectId == 0) continue;

            var floors = newConn.Query<FloorRow>(
                "SELECT Id, FloorName, ImagePath FROM Floors WHERE ProjectId = @Id",
                new { Id = projectId }).ToList();

            for (int i = 0; i < imageColumns.Length; i++)
            {
                var floor = floors.FirstOrDefault(f => f.FloorName == floorNames[i]);
                if (floor is null) continue;
                if (!string.IsNullOrWhiteSpace(floor.ImagePath)) continue; // never overwrite a chosen image

                var legacyPath = Str(rdr.GetValue(imageColumns[i]));
                if (string.IsNullOrWhiteSpace(legacyPath)) continue;

                var fileName = Path.GetFileName(legacyPath.Trim());
                if (string.IsNullOrWhiteSpace(fileName)) continue;

                var sourcePath = Path.Combine(imagesRoot, fileName);
                if (!File.Exists(sourcePath))
                {
                    result.ImageMissing++;
                    continue;
                }

                var destPath = Path.Combine(newImagesDir,
                    $"{projectId}_{floor.Id}{Path.GetExtension(fileName).ToLowerInvariant()}");
                if (!File.Exists(destPath))
                {
                    try
                    {
                        File.Copy(sourcePath, destPath, overwrite: false);
                    }
                    catch
                    {
                        result.ImageMissing++;
                        continue;
                    }
                }

                newConn.Execute("UPDATE Floors SET ImagePath = @Path WHERE Id = @Id",
                    new { Path = destPath, Id = floor.Id });
                result.ImageCount++;
            }
        }
    }

    private class FloorRow
    {
        public int Id { get; set; }
        public string FloorName { get; set; } = string.Empty;
        public string? ImagePath { get; set; }
    }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ProjectCount { get; set; }
    public int WindowCount { get; set; }
    public int GlazingCount { get; set; }
    public int FrameCount { get; set; }
    public int RangeCount { get; set; }
    public int TypeCount { get; set; }
    public int ClimateZoneRowCount { get; set; }
    public int ImageCount { get; set; }
    public int ImageMissing { get; set; }
    public bool ProjectsExisted { get; set; }

    public string Summary => !Success
        ? $"Migration failed: {Error}"
        : ProjectsExisted
            ? $"Import complete: projects already imported; images imported: {ImageCount}" +
              (ImageMissing > 0 ? $" ({ImageMissing} missing)" : "") + "."
            : $"Migration complete: {ProjectCount} projects, {WindowCount} windows, {GlazingCount} glazing types, " +
              $"{FrameCount} frames, {RangeCount} ranges, {TypeCount} window types, {ClimateZoneRowCount} climate zone rows, " +
              $"{ImageCount} images copied ({ImageMissing} missing).";
}
