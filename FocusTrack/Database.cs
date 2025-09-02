using FocusTrack.Model;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;

namespace FocusTrack
{
    public static class Database
    {
        private static string DbFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usage.db");
        private static string ConnString => $"Data Source={DbFile};Version=3;DateTimeKind=Utc;DateTimeFormat=ISO8601;";

        public static void Initialize()
        {
            if (!File.Exists(DbFile))
            {
                SQLiteConnection.CreateFile(DbFile);
            }

            using (var conn = new SQLiteConnection(ConnString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS AppUsage (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            AppName TEXT,
                            AppIcon BLOB,
                            WindowTitle TEXT,
                            StartTime TEXT,
                            EndTime TEXT,
                            DurationSeconds INTEGER,
                            ExePath TEXT
                        );";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static async Task SaveSessionAsync(string appName, string windowTitle, DateTime start, DateTime end, string exePath)
        {
            byte[] iconBytes = null;

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    using (var icon = Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (icon != null)
                        {
                            using (var bmp = icon.ToBitmap())
                            using (var ms = new MemoryStream())
                            {
                                bmp.Save(ms, ImageFormat.Png);
                                iconBytes = ms.ToArray();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to extract icon: " + ex.Message);
                }
            }

            using (var conn = new SQLiteConnection(ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO AppUsage(AppName, WindowTitle, StartTime, EndTime, AppIcon, DurationSeconds, ExePath)
                        VALUES (@AppName, @WindowTitle, @StartTime, @EndTime, @AppIcon, @DurationSeconds, @ExePath)";

                    cmd.Parameters.AddWithValue("@AppName", appName);
                    cmd.Parameters.AddWithValue("@WindowTitle", windowTitle);
                    cmd.Parameters.AddWithValue("@StartTime", start.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@EndTime", end.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@DurationSeconds", (int)(end - start).TotalSeconds);
                    cmd.Parameters.AddWithValue("@AppIcon", (object)iconBytes ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ExePath", string.IsNullOrEmpty(exePath) ? (object)DBNull.Value : exePath);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }


        public static async Task<List<AppUsage>> GetAllDataAsync()
        {
            var result = new List<AppUsage>();

            using (var connection = new SQLiteConnection(ConnString))
            {
                await connection.OpenAsync();

                string query = @"SELECT Id, AppName, WindowTitle, StartTime, EndTime, DurationSeconds, AppIcon, ExePath
                         FROM AppUsage ORDER BY StartTime DESC";

                using (var command = new SQLiteCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new AppUsage
                        {
                            Id = reader.GetInt32(0),
                            AppName = reader.GetString(1),
                            WindowTitle = reader.GetString(2),
                            StartTime = DateTime.TryParse(reader.GetString(3), out var st) ? st : DateTime.MinValue,
                            EndTime = DateTime.TryParse(reader.GetString(4), out var et) ? et : DateTime.MinValue,
                            Duration = TimeSpan.FromSeconds(reader.GetInt32(5)),
                            AppIcon = reader.IsDBNull(6) ? null : reader.GetFieldValue<byte[]>(6),
                            ExePath = reader.IsDBNull(7) ? null : reader.GetString(7)
                        });
                    }
                }
            }

            return result;
        }

    }
}
