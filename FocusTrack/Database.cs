using FocusTrack.Model;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xceed.Wpf.Toolkit.Primitives;
using static SkiaSharp.HarfBuzz.SKShaper;

namespace FocusTrack
{
    public static class Database
    {
        private static string DbFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usage.db");
        private static string ConnString => $"Data Source={DbFile};Version=3;DateTimeKind=Utc;DateTimeFormat=ISO8601;";

        public static void Initialize()
        {
            try
            {
                // Create database file if missing
                if (!File.Exists(DbFile))
                {
                    SQLiteConnection.CreateFile(DbFile);
                }

                using (var conn = new SQLiteConnection(ConnString))
                {
                    conn.Open();

                    using (var cmd = conn.CreateCommand())
                    {
                        // Always run CREATE TABLE IF NOT EXISTS
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Database initialization failed: " + ex.Message);
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


        public class HourlyUsage
        {
            public DateTime Date { get; set; }
            public int Hour { get; set; }  // 0-23
            public int TotalSeconds { get; set; }

            public double TotalMinutes => TotalSeconds / 60.0; // Convenience property
        }

        public static async Task<List<HourlyUsage>> GetHourlyUsageAsync(DateTime start, DateTime end)
        {
            var result = new int[24]; // Index = hour 0-23

            using (var conn = new SQLiteConnection(Database.ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT StartTime, DurationSeconds
                FROM AppUsage
                WHERE StartTime <= @end AND EndTime >= @start";
                    cmd.Parameters.AddWithValue("@start", start);
                    cmd.Parameters.AddWithValue("@end", end);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var startTime = reader.GetDateTime(0);
                            var duration = reader.GetInt32(1);
                            var endTime = startTime.AddSeconds(duration);

                            // Clip to requested range
                            if (startTime < start) startTime = start;
                            if (endTime > end) endTime = end;

                            var tempStart = startTime;

                            while (tempStart < endTime)
                            {
                                int hour = tempStart.Hour;

                                // End of this hour
                                var nextHour = new DateTime(tempStart.Year, tempStart.Month, tempStart.Day, hour, 0, 0).AddHours(1);
                                var chunkEnd = nextHour < endTime ? nextHour : endTime;

                                var seconds = (chunkEnd - tempStart).TotalSeconds;
                                result[hour] += (int)seconds;

                                tempStart = chunkEnd;
                            }
                        }
                    }
                }
            }

            // Return total usage per hour-of-day
            return Enumerable.Range(0, 24)
                .Select(h => new HourlyUsage
                {
                    Hour = h,
                    TotalSeconds = result[h]
                })
                .ToList();
        }




        // Internal class to hold raw data
        private class AppUsageRaw
        {
            public string AppName { get; set; }
            public string ExePath { get; set; }
            public byte[] AppIcon { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }

        public static async Task<List<AppUsage>> GetAllAppUsageAsync(DateTime? start, DateTime? end)
        {
            var rawData = new List<AppUsageRaw>();

            try
            {
                using (var conn = new SQLiteConnection(ConnString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                    SELECT AppName, ExePath, AppIcon, StartTime, EndTime
                    FROM AppUsage
                    WHERE (@start IS NULL OR EndTime >= @start) AND (@end IS NULL OR StartTime <= @end)
                    ORDER BY StartTime;
                    ";

                        cmd.Parameters.AddWithValue("@start", (object)start ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@end", (object)end ?? DBNull.Value);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                if (DateTime.TryParse(reader["StartTime"].ToString(), out var startTime) &&
                                    DateTime.TryParse(reader["EndTime"].ToString(), out var endTime) &&
                                    endTime > startTime)  // Sanity check
                                {
                                    rawData.Add(new AppUsageRaw
                                    {
                                        AppName = reader["AppName"].ToString(),
                                        ExePath = reader["ExePath"] as string,
                                        AppIcon = reader["AppIcon"] as byte[],
                                        StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Utc),
                                        EndTime = DateTime.SpecifyKind(endTime, DateTimeKind.Utc)
                                    });
                                }
                            }
                        }
                    }
                }

                // 1️⃣ Debug: Raw data
                System.Diagnostics.Debug.WriteLine("=== Raw Data ===");
                foreach (var session in rawData)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Raw -> App: {session.AppName}, Start: {session.StartTime}, End: {session.EndTime}");
                }

                // 2️⃣ Split sessions across dates
                // 1️⃣ Split sessions across dates
                var splitData = new List<AppUsage>();
                foreach (var session in rawData)
                {
                    var current = session.StartTime;
                    while (current.Date <= session.EndTime.Date)
                    {
                        var dayEnd = current.Date.AddDays(1);
                        var segmentEnd = session.EndTime < dayEnd ? session.EndTime : dayEnd;
                        var duration = segmentEnd - current;

                        if (duration.TotalSeconds <= 0)
                            break;  // Prevent invalid data

                        splitData.Add(new AppUsage
                        {
                            AppName = session.AppName,
                            ExePath = session.ExePath,
                            AppIcon = session.AppIcon,
                            Date = current.Date,
                            Duration = duration
                        });

                        current = segmentEnd;
                    }
                }

                // 2️⃣ Filter splitData to only include the requested date range
                splitData = splitData
                    .Where(x => (!start.HasValue || x.Date >= start.Value.Date) &&
                                (!end.HasValue || x.Date <= end.Value.Date))
                    .ToList();

                // 3️⃣ Group by AppName + Date
                var groupedData = splitData
                    .GroupBy(x => new { x.AppName, x.Date })  // Use AppName + Date as key
                    .Select(g =>
                    {
                        var newestRecord = g.OrderByDescending(x => x.Date).ThenByDescending(x => x.Duration).First();

                        string friendlyAppName = AppFriendlyNames.ContainsKey(newestRecord.AppName)
                            ? AppFriendlyNames[newestRecord.AppName]
                            : newestRecord.AppName;

                        var totalDuration = TimeSpan.FromSeconds(g.Sum(x => x.Duration.TotalSeconds));

                        // Debug
                        System.Diagnostics.Debug.WriteLine($"[Grouped] Date: {g.Key.Date:dd-MM-yyyy}, App: {friendlyAppName}, TotalDuration: {totalDuration}");

                        return new AppUsage
                        {
                            AppName = friendlyAppName,
                            ExePath = newestRecord.ExePath,
                            AppIcon = newestRecord.AppIcon,
                            Date = g.Key.Date,
                            Duration = totalDuration
                        };
                    })
                    .OrderByDescending(x => x.Duration)
                    .ToList();


                // 4️⃣ Debug: Final list that will go to UI
                System.Diagnostics.Debug.WriteLine("=== Final Data to UI ===");
                foreach (var item in groupedData)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"UI -> App: {item.AppName}, Duration: {item.Duration}");
                }

                return groupedData;

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetAllAppUsageAsync: {ex.Message}");
                return new List<AppUsage>();  // Return empty list on failure
            }
        }


        private static readonly Dictionary<string, string> AppFriendlyNames = new Dictionary<string, string>()
        {
             { "chrome", "Google Chrome" },
                        { "msedge", "Microsoft Edge" },
                        { "firefox", "Mozilla Firefox" },
                        { "opera", "Opera Browser" },
                        { "iexplore", "Internet Explorer" },
                        { "code", "Visual Studio Code" },
                        { "devenv", "Visual Studio" },
                        { "sublime_text", "Sublime Text" },
                        { "pycharm", "PyCharm" },
                        { "webstorm", "WebStorm" },
                        { "androidstudio", "Android Studio" },
                        { "eclipse", "Eclipse IDE" },
                        { "intellij", "IntelliJ IDEA" },
                        { "notepad", "Notepad" },
                        { "notepad++", "Notepad++" },
                        { "winword", "Microsoft Word" },
                        { "excel", "Microsoft Excel" },
                        { "powerpnt", "Microsoft PowerPoint" },
                        { "outlook", "Microsoft Outlook" },
                        { "onenote", "Microsoft OneNote" },
                        { "access", "Microsoft Access" },
                        { "teams", "Microsoft Teams" },
                        { "zoom", "Zoom" },
                        { "slack", "Slack" },
                        { "discord", "Discord" },
                        { "skype", "Skype" },
                        { "telegram", "Telegram" },
                        { "whatsapp", "WhatsApp" },
                        { "spotify", "Spotify" },
                        { "vlc", "VLC Media Player" },
                        { "wmplayer", "Windows Media Player" },
                        { "itunes", "iTunes" },
                        { "foobar2000", "Foobar2000" },
                        { "studio64", "Android Studio" }
        };


        public static async Task<List<AppUsage>> GetAppDetailResultsByAppName(
    string appName, DateTime date, DateTime? start = null, DateTime? end = null)
        {
            var rawData = new List<AppUsage>();

            try
            {
                using (var conn = new SQLiteConnection(ConnString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        // Default to whole day if start/end not provided
                        if (!start.HasValue) start = date.Date;
                        if (!end.HasValue) end = date.Date.AddDays(1).AddTicks(-1);

                        cmd.CommandText = @"
                    SELECT WindowTitle, StartTime, EndTime
                    FROM AppUsage
                    WHERE LOWER(AppName) = LOWER(@appName)
                      AND EndTime >= @start AND StartTime <= @end
                    ORDER BY StartTime";

                        cmd.Parameters.AddWithValue("@appName", appName);
                        cmd.Parameters.AddWithValue("@start", start);
                        cmd.Parameters.AddWithValue("@end", end);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                if (DateTime.TryParse(reader["StartTime"].ToString(), out var startTime) &&
                                    DateTime.TryParse(reader["EndTime"].ToString(), out var endTime))
                                {
                                    rawData.Add(new AppUsage
                                    {
                                        WindowTitle = reader["WindowTitle"].ToString(),
                                        StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Utc),
                                        EndTime = DateTime.SpecifyKind(endTime, DateTimeKind.Utc)
                                    });
                                }
                            }
                        }
                    }
                }

                // Split sessions across days
                var splitData = new List<AppUsage>();
                foreach (var session in rawData)
                {
                    var current = session.StartTime;
                    while (current.Date <= session.EndTime.Date)
                    {
                        var dayEnd = current.Date.AddDays(1);
                        var segmentEnd = session.EndTime < dayEnd ? session.EndTime : dayEnd;
                        var duration = segmentEnd - current;

                        if (duration.TotalSeconds <= 0) break;

                        splitData.Add(new AppUsage
                        {
                            WindowTitle = session.WindowTitle,
                            StartTime = current,
                            EndTime = segmentEnd,
                            Duration = duration
                        });

                        current = segmentEnd;
                    }
                }

                // Group by WindowTitle and sum durations
                var groupedData = splitData
                    .Where(x => x.StartTime.Date >= start.Value.Date && x.EndTime.Date <= end.Value.Date)
                    .GroupBy(x => x.WindowTitle)
                    .Select(g =>
                    {
                        var minStart = g.Min(x => x.StartTime);
                        var maxEnd = g.Max(x => x.EndTime);
                        var totalDuration = TimeSpan.FromSeconds(g.Sum(x => x.Duration.TotalSeconds));

                        return new AppUsage
                        {
                            WindowTitle = g.Key,
                            StartTime = minStart,
                            EndTime = maxEnd,
                            Duration = totalDuration
                        };
                    })
                    .OrderByDescending(x => x.Duration)
                    .ToList();

                return groupedData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetAppDetailResultsByAppName: {ex.Message}");
                return new List<AppUsage>();
            }
        }




    }
}
