using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace FenCalc2.Data;

public static class Database
{
    private static string? _connectionString;

    public static string GetDatabasePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("FENCALC2_DB_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
            return overridePath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "FenCalc2", "Data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "FenCalc2.db");
    }

    public static string GetConnectionString()
    {
        if (_connectionString is null)
        {
            var path = GetDatabasePath();
            // Foreign Keys=True is REQUIRED: ON DELETE CASCADE (Floors/WindowPlacements
            // → Projects) is what wipes a deleted building's child rows — without the
            // pragma a delete would silently orphan them.
            _connectionString = $"Data Source={path};Foreign Keys=True";
        }
        return _connectionString;
    }

    public static SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(GetConnectionString());
        conn.Open();
        return conn;
    }
}
