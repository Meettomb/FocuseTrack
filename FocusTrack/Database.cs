using FocusTrack.Model;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

        public class HourlyUsage
        {
            public int Hour { get; set; }  // 0-23
            public int TotalSeconds { get; set; }
        }

        public static async Task<List<HourlyUsage>> GetHourlyUsageAsync(DateTime start, DateTime end)
        {
            var result = new List<HourlyUsage>();

            // Query DB
            using (var conn = new System.Data.SQLite.SQLiteConnection(Database.ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT strftime('%H', StartTime) as Hour,
                       SUM(DurationSeconds) as TotalDuration
                FROM AppUsage
                WHERE StartTime BETWEEN @start AND @end
                GROUP BY Hour
                ORDER BY Hour";

                    cmd.Parameters.AddWithValue("@start", start);
                    cmd.Parameters.AddWithValue("@end", end);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            result.Add(new HourlyUsage
                            {
                                Hour = int.Parse(reader.GetString(0)),
                                TotalSeconds = reader.GetInt32(1)
                            });
                        }
                    }
                }
            }

            // --- Ensure all 24 hours exist ---
            var fullDay = Enumerable.Range(0, 24)
                .Select(h =>
                {
                    var existing = result.FirstOrDefault(r => r.Hour == h);
                    return new HourlyUsage
                    {
                        Hour = h,
                        TotalSeconds = existing != null ? existing.TotalSeconds : 0
                    };
                })
                .ToList();


            return fullDay;
        }




    }
}
