using System;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;

namespace FenCalc2.Data;

public static class Migrations
{
    public static void Run(SqliteConnection conn)
    {
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER PRIMARY KEY
            );
            """);

        var currentVersion = GetCurrentVersion(conn);

        if (currentVersion < 1)
        {
            RunV1(conn);
            SetVersion(conn, 1);
        }

        if (currentVersion < 2)
        {
            RunV2(conn);
            SetVersion(conn, 2);
        }

        if (currentVersion < 3)
        {
            RunV3(conn);
            SetVersion(conn, 3);
        }

        if (currentVersion < 4)
        {
            RunV4(conn);
            SetVersion(conn, 4);
        }
    }

    private static int GetCurrentVersion(SqliteConnection conn)
    {
        try
        {
            return conn.ExecuteScalar<int>("SELECT MAX(Version) FROM SchemaVersion");
        }
        catch
        {
            return 0;
        }
    }

    private static void SetVersion(SqliteConnection conn, int version)
    {
        conn.Execute("INSERT INTO SchemaVersion (Version) VALUES (@Version)", new { Version = version });
    }

    private static void RunV2(SqliteConnection conn)
    {
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """);
    }

    private static readonly string[] CompassNames =
        { "North", "North East", "East", "South East", "South", "South West", "West", "North West" };

    // v3: window Orientation becomes image-relative (plan slot) instead of a true compass
    // direction; Projects.Orientation becomes the north anchor (which plan slot is true
    // North). Existing true-direction values are reconverted per project anchor:
    // planSlot = (trueIdx - anchorIdx) mod 8. Add per-project rotation/ mirror state.
    private static void RunV3(SqliteConnection conn)
    {
        conn.Execute("ALTER TABLE Projects ADD COLUMN RotationOffset INTEGER NOT NULL DEFAULT 0;");
        conn.Execute("ALTER TABLE Projects ADD COLUMN IsMirrored INTEGER NOT NULL DEFAULT 0;");

        var projects = conn.Query<AnchorRow>("SELECT Id, Orientation FROM Projects").ToList();
        foreach (var p in projects)
        {
            var anchorIdx = Array.IndexOf(CompassNames, p.Orientation);
            if (anchorIdx < 0) anchorIdx = 0;

            var placements = conn.Query<PlacementRow>(
                "SELECT Id, Orientation FROM WindowPlacements WHERE ProjectId = @Id", new { p.Id }).ToList();
            foreach (var wp in placements)
            {
                var idx = Array.IndexOf(CompassNames, wp.Orientation);
                if (idx < 0) continue;
                var planSlot = CompassNames[(idx - anchorIdx + 8) % 8];
                conn.Execute("UPDATE WindowPlacements SET Orientation = @Slot WHERE Id = @Id",
                    new { Slot = planSlot, wp.Id });
            }
        }
    }

    private class AnchorRow
    {
        public int Id { get; set; }
        public string Orientation { get; set; } = "North";
    }

    private class PlacementRow
    {
        public int Id { get; set; }
        public string Orientation { get; set; } = string.Empty;
    }

    // v4: window types can store manufacturer-tested U/SHGC overrides; null = use
    // the GlazingPerformance defaults for the glass/frame combination.
    private static void RunV4(SqliteConnection conn)
    {
        conn.Execute("ALTER TABLE WindowTypes ADD COLUMN UValue REAL;");
        conn.Execute("ALTER TABLE WindowTypes ADD COLUMN SHGC REAL;");
    }

    private static void RunV1(SqliteConnection conn)
    {
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS Projects (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                BuildingName    TEXT NOT NULL,
                ClientName      TEXT NOT NULL,
                ProjectName     TEXT NOT NULL,
                ClimateZone     TEXT NOT NULL DEFAULT 'Zone 1',
                Orientation     TEXT NOT NULL DEFAULT 'North',
                UNIQUE(BuildingName, ClientName, ProjectName)
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS Floors (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId   INTEGER NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
                FloorName   TEXT NOT NULL,
                FloorArea   REAL,
                ImagePath   TEXT,
                UNIQUE(ProjectId, FloorName)
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS WindowPlacements (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId   INTEGER NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
                FloorId     INTEGER NOT NULL REFERENCES Floors(Id) ON DELETE CASCADE,
                WinType     TEXT NOT NULL,
                WinWidth    REAL NOT NULL,
                WinHeight   REAL NOT NULL,
                Orientation TEXT NOT NULL,
                WinGlass    TEXT NOT NULL,
                WinFrame    TEXT NOT NULL,
                UValue      REAL NOT NULL,
                SHGC        REAL NOT NULL,
                P           INTEGER NOT NULL DEFAULT 375,
                G           INTEGER NOT NULL DEFAULT 160,
                RoomName    TEXT
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS WindowRanges (
                WinRange TEXT PRIMARY KEY
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS WindowTypes (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                WinRange    TEXT NOT NULL REFERENCES WindowRanges(WinRange) ON DELETE CASCADE,
                WinType     TEXT NOT NULL,
                WinWidth    REAL,
                WinHeight   REAL,
                WinGlass    TEXT,
                WinFrame    TEXT,
                UNIQUE(WinRange, WinType)
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS GlazingPerformance (
                Glass       TEXT PRIMARY KEY,
                UValue_Steel  REAL,
                SHGC_Steel    REAL,
                UValue_Other  REAL,
                SHGC_Other    REAL
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS WindowFrames (
                FrameType TEXT PRIMARY KEY
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS ClimateZones (
                Zone        INTEGER NOT NULL,
                PH          TEXT NOT NULL,
                North       REAL,
                NorthEast   REAL,
                East        REAL,
                SouthEast   REAL,
                South       REAL,
                SouthWest   REAL,
                West        REAL,
                NorthWest   REAL,
                PRIMARY KEY (Zone, PH)
            );
            """);
    }
}
