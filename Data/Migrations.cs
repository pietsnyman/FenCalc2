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

        if (currentVersion < 5)
        {
            RunV5(conn);
            SetVersion(conn, 5);
        }

        if (currentVersion < 6)
        {
            RunV6(conn);
            SetVersion(conn, 6);
        }

        if (currentVersion < 7)
        {
            RunV7(conn);
            SetVersion(conn, 7);
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

    // v5: SANS 10400-XA:2026 support - per-project standard edition (existing projects
    // keep the legacy '2011' report), site data (town/latitude/SCCP) feeding the table 3
    // shading multiplier and the 5.3.7 condensation note, an optional per-window shading
    // override, and the reference tables that Xa2026Seeder fills from the generated
    // Data/seed/xa2026.sql on every startup.
    private static void RunV5(SqliteConnection conn)
    {
        conn.Execute("""
            ALTER TABLE Projects ADD COLUMN StandardEdition TEXT NOT NULL DEFAULT '2011';
            ALTER TABLE Projects ADD COLUMN Town TEXT;
            ALTER TABLE Projects ADD COLUMN Latitude REAL;
            ALTER TABLE Projects ADD COLUMN Sccp INTEGER NOT NULL DEFAULT 0;
            """);
        // NULL = automatic per 5.2.2 (P >= H x M); 1 = force shaded; 0 = force unshaded.
        conn.Execute("ALTER TABLE WindowPlacements ADD COLUMN ShadingOverride INTEGER;");

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS FenestrationBands (
                RatioMax      REAL PRIMARY KEY,  -- %; 100 = the ">60 %" row of table 4
                MaxU          REAL,              -- NULL = "Any solution"
                ShadedShgc    REAL,
                UnshadedShgc  REAL,
                SouthernShgc  REAL               -- NULL (always "Any solution" in 2026)
            );
            CREATE TABLE IF NOT EXISTS ShadingMultipliers (
                LatitudeMax   REAL PRIMARY KEY,  -- degrees South; 999 = ">32" row
                Multiplier    REAL NOT NULL
            );
            CREATE TABLE IF NOT EXISTS EnergyZoneTowns (
                Town        TEXT NOT NULL,
                Province    TEXT NOT NULL,
                Longitude   REAL,
                Latitude    REAL NOT NULL,
                EnergyZone  TEXT NOT NULL,       -- '1'..'7' or '5H'
                Sccp        INTEGER NOT NULL,
                PRIMARY KEY (Town, Province)
            );
            """);
    }

    // v6: external wall compliance (XA:2026 cl. 5.5) — named wall assemblies per
    // project, ordered layers with per-layer manufacturer overrides, the material
    // library and the per-zone Table 6/7 requirements (rows applied by Xa2026Seeder
    // from the generated xa2026.sql).
    private static void RunV6(SqliteConnection conn)
    {
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS WallAssemblies (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId          INTEGER NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
                Name               TEXT NOT NULL,
                Category1          INTEGER NOT NULL DEFAULT 0,
                AttachmentMode     TEXT NOT NULL DEFAULT 'None',  -- None|Included|Excluded (5.5.5)
                UNIQUE(ProjectId, Name)
            );
            CREATE TABLE IF NOT EXISTS WallMaterials (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT NOT NULL UNIQUE,
                Category    TEXT NOT NULL,   -- Film|Cavity|Plaster|Masonry|Insulation|Metal|Other
                Lambda      REAL,            -- W/m.K; NULL for fixed-resistance films/cavity
                Density     REAL,            -- kg/m3
                CSpec       REAL,            -- J/kg.K (specific heat for C-value / CR)
                FixedR      REAL,            -- m2.K/W when the resistance is fixed
                PlasterFree INTEGER NOT NULL DEFAULT 0,  -- table 6 note a: no plaster needed
                Presets     TEXT             -- comma list of standard thicknesses (mm)
            );
            CREATE TABLE IF NOT EXISTS WallZoneRequirements (
                EnergyZone        TEXT PRIMARY KEY,  -- '1'..'7' or '5H'
                HeavyMinR         REAL,              -- table 6 (density >= 270 kg/m2)
                HeavyConstruction TEXT,              -- '50 mm cavity wall' | 'Collar jointed wall'
                LightMinR         REAL,              -- table 7 (density < 270): pass on R OR CR
                LightMinCR        REAL               -- CR in hours (cl. 3.5)
            );
            CREATE TABLE IF NOT EXISTS WallLayers (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                WallId          INTEGER NOT NULL REFERENCES WallAssemblies(Id) ON DELETE CASCADE,
                Pos             INTEGER NOT NULL,   -- 0 = outermost (outside)
                MaterialId      INTEGER REFERENCES WallMaterials(Id),
                Name            TEXT NOT NULL,
                Category        TEXT NOT NULL,
                ThicknessMm     REAL NOT NULL,
                LambdaOverride  REAL,               -- manufacturer value; NULL = library
                DensityOverride REAL,
                COverride       REAL
            );
            """);
    }

    // v7: the walls page is edition-independent (the user mixes 2011/2026 builds
    // deliberately); WallEnergyZone carries the SITE's energy zone for tables 6/7 —
    // on a 2011 building Projects.ClimateZone is a CLIMATIC zone whose number means
    // something else, so it must never drive the wall tables.
    private static void RunV7(SqliteConnection conn)
    {
        conn.Execute("ALTER TABLE Projects ADD COLUMN WallEnergyZone TEXT;");
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
