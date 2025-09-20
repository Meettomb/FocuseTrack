using FocusTrack.model;
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

                        // Create UserSettings table
                        cmd.CommandText = @"
                            CREATE TABLE IF NOT EXISTS UserSettings  (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                TrackPrivateMode BOOLEAN DEFAULT 1,
                                TrackVPN BOOLEAN DEFAULT 1
                            );";
                        cmd.ExecuteNonQuery();

                        // Ensure at least one row exists
                        cmd.CommandText = @"
                            INSERT INTO UserSettings (TrackPrivateMode, TrackVPN)
                            SELECT 1, 1
                            WHERE NOT EXISTS (SELECT 1 FROM UserSettings);
                        ";
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

        public class TrackingFilters
        {
            // Readonly so it cannot be modified accidentally
            public static readonly string[] BlockedKeywords =
            {
                "[inprivate]",
                "[incognito]",
                "private browsing",
                "sex",
                "porn",
                "hot",
                "xxx"
            };
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
                SELECT StartTime, DurationSeconds, WindowTitle
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
                            var windowTitle = reader["WindowTitle"]?.ToString() ?? "";

                            if (TrackingFilters.BlockedKeywords.Any(k =>
                            windowTitle.IndexOf(k, StringComparison.OrdinalIgnoreCase) > 0)) {
                                continue;
                            }

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



        private class AppUsageRaw
        {
            public string AppName { get; set; }
            public string ExePath { get; set; }
            public byte[] AppIcon { get; set; }
            public string WindowTitle { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }
        public static async Task<List<AppUsage>> GetAllAppUsageAsync(DateTime? start, DateTime? end)
        {
            try
            {
                // 1️⃣ Fetch raw data from database
                var rawData = new List<AppUsageRaw>();
                using (var conn = new SQLiteConnection(ConnString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                    SELECT AppName, ExePath, AppIcon, WindowTitle, StartTime, EndTime
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
                                    endTime > startTime)
                                {
                                    rawData.Add(new AppUsageRaw
                                    {
                                        AppName = reader["AppName"].ToString(),
                                        ExePath = reader["ExePath"] as string,
                                        AppIcon = reader["AppIcon"] as byte[],
                                        WindowTitle = reader["WindowTitle"] as string,
                                        StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Utc),
                                        EndTime = DateTime.SpecifyKind(endTime, DateTimeKind.Utc)
                                    });
                                }
                            }
                        }
                    }
                }

                // Use a list of tuples to reduce memory overhead
                var splitData = new List<AppUsage>();
                foreach (var session in rawData)
                {
                    // Skip session if AppName or Title contains blocked keyword
                    if (TrackingFilters.BlockedKeywords.Any(k =>
                            (session.WindowTitle?.ToLowerInvariant().Contains(k) ?? false)))
                        continue;



                    DateTime current = session.StartTime;
                    DateTime sessionEnd = session.EndTime;


                    while (current.Date <= sessionEnd.Date)
                    {
                        DateTime dayEnd = current.Date.AddDays(1);
                        DateTime segmentEnd = sessionEnd < dayEnd ? sessionEnd : dayEnd;
                        TimeSpan duration = segmentEnd - current;

                        if (duration.TotalSeconds <= 0)
                            break;

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

                // 3️⃣ Filter only the requested range
                if (start.HasValue) splitData = splitData.Where(x => x.Date >= start.Value.Date).ToList();
                if (end.HasValue) splitData = splitData.Where(x => x.Date <= end.Value.Date).ToList();

                // 4️⃣ Determine if single-day or multi-day grouping
                // Determine if single-day or multi-day grouping
                IEnumerable<IGrouping<object, AppUsage>> groupedData;

                bool isSingleDayRange = start.HasValue && end.HasValue && start.Value.Date == end.Value.Date;

                if (isSingleDayRange)
                {
                    groupedData = splitData.GroupBy(x => (object)new { x.AppName, x.Date });
                }
                else
                {
                    groupedData = splitData.GroupBy(x => (object)x.AppName);
                }

                // Now build final result
                var result = groupedData.Select(g =>
                {
                    var newestRecord = g.OrderByDescending(x => x.Date).ThenByDescending(x => x.Duration).First();

                    string friendlyAppName = AppFriendlyNames.ContainsKey(newestRecord.AppName)
                        ? AppFriendlyNames[newestRecord.AppName]
                        : newestRecord.AppName;

                    var totalDuration = TimeSpan.FromSeconds(g.Sum(x => x.Duration.TotalSeconds));

                    DateTime dateValue = isSingleDayRange
                        ? ((dynamic)g.Key).Date
                        : g.Max(x => x.Date);

                    return new AppUsage
                    {
                        AppName = friendlyAppName,
                        ExePath = newestRecord.ExePath,
                        AppIcon = newestRecord.AppIcon,
                        Date = dateValue,
                        Duration = totalDuration
                    };
                })
                .OrderByDescending(x => x.Duration)
                .ToList();


                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetAllAppUsageAsync: {ex.Message}");
                return new List<AppUsage>();
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


        public static async Task<List<AppUsage>> GetAppDetailResultsByAppName(string appName, DateTime date, DateTime? start = null, DateTime? end = null)
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
                    var userSettingList = await GetUserSettings();
                    var TrackPrivateMode = userSettingList[0].TrackPrivateMode;
                    if (TrackPrivateMode == false)
                    {

                        if (TrackingFilters.BlockedKeywords.Any(k =>
                            (session.WindowTitle?.ToLowerInvariant().Contains(k) ?? false)))
                            continue;
                    }

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
                    .Where(x => x.EndTime > start.Value && x.StartTime < end.Value)
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


        public static async Task<List<UserSettings>> GetUserSettings()
        {
            var rawSettings = new List<UserSettings>();

            using (var conn = new SQLiteConnection(ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, TrackPrivateMode, TrackVPN FROM UserSettings LIMIT 1";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            rawSettings.Add(new UserSettings
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                TrackPrivateMode = Convert.ToBoolean(reader["TrackPrivateMode"]),
                                TrackVPN = Convert.ToBoolean(reader["TrackVPN"])
                            });
                        }
                    }
                }
            }
            return rawSettings;
        }
        public static async Task UpdateTrackPrivateModeAsync(bool value)
        {
            using (var conn = new SQLiteConnection(ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE UserSettings SET TrackPrivateMode = @value WHERE Id = 1";
                    cmd.Parameters.AddWithValue("@value", value ? 1 : 0);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        public static async Task UpdateTrackVPNAsync(bool value)
        {
            using (var conn = new SQLiteConnection(ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE UserSettings SET TrackVPN = @value WHERE Id = 1";
                    cmd.Parameters.AddWithValue("@value", value ? 1 : 0);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }



        public class AppOpenCount
        {
            public string AppName { get; set; }
            public DateTime Day { get; set; }
            public int OpenCount { get; set; }
            public byte[] AppIcon { get; set; }
            public int TotalCount { get; set; }
        }

        public static async Task<List<AppOpenCount>> GetAppOpenCountAsync(DateTime? start, DateTime? end, int gapThresholdSeconds = 10)
        {
            var counts = new List<AppOpenCount>();

            using (var conn = new SQLiteConnection(ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT AppName, StartTime, EndTime, DurationSeconds, AppIcon
                FROM AppUsage
                WHERE EndTime >= @start AND StartTime <= @end
                      AND DurationSeconds > 0
                ORDER BY AppName, StartTime
            ";

                    cmd.Parameters.AddWithValue("@start", start);
                    cmd.Parameters.AddWithValue("@end", end);

                    var data = new List<(string AppName, DateTime Start, DateTime End, byte[] AppIcon)>();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add((
                                reader["AppName"].ToString(),
                                DateTime.Parse(reader["StartTime"].ToString()),
                                DateTime.Parse(reader["EndTime"].ToString()),
                                reader["AppIcon"] as byte[]
                            ));
                        }
                    }

                    string lastApp = null;
                    DateTime? lastEnd = null;

                    foreach (var row in data)
                    {
                        if (row.AppName != lastApp || (lastEnd.HasValue && (row.Start - lastEnd.Value).TotalSeconds > gapThresholdSeconds))
                        {
                            // New session for this app
                            var existing = counts.FirstOrDefault(c => c.AppName == row.AppName);
                            if (existing != null)
                                existing.OpenCount++;
                            else
                                counts.Add(new AppOpenCount
                                {
                                    AppName = row.AppName,
                                    Day = start ?? DateTime.Today,
                                    OpenCount = 1,
                                    AppIcon = row.AppIcon
                                });
                        }

                        lastApp = row.AppName;
                        lastEnd = row.End;
                    }
                }
            }

            return counts.OrderByDescending(c => c.OpenCount).ToList();
        }



    }
}
