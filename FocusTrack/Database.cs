
using FocusTrack.model;
using FocusTrack.Model;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
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

                        // AppIcons table (create first, since others reference it)
                        cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS AppIcons (
                            AppIconId INTEGER PRIMARY KEY AUTOINCREMENT,
                            AppName TEXT UNIQUE,
                            AppIcon BLOB
                        );";
                        cmd.ExecuteNonQuery();


                        // AppUsage table
                        cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS AppUsage (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            AppName TEXT,
                            AppIcon INTEGER,
                            WindowTitle TEXT,
                            StartTime TEXT,
                            EndTime TEXT,
                            DurationSeconds INTEGER,
                            ExePath TEXT,
                            ForEIGN KEY(AppIcon) REFERENCES AppIcons(AppIconId)
                        );";
                        cmd.ExecuteNonQuery();

                        // UserSettings table
                        cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS UserSettings (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            TrackPrivateMode INTEGER DEFAULT 1,
                            TrackVPN INTEGER DEFAULT 1,
                            BreakTime TEXT DEFAULT '00:00',
                            NotifyBreakEveryTime INTEGER DEFAULT 0,
                            ActivityTrackingScope INTEGER DEFAULT 0
                        );";
                        // 0 = Active Apps Only AND 1 = Entire Screen, In ActivityTrackingScope
                        cmd.ExecuteNonQuery();



                        // Ensure at least one row exists
                        cmd.CommandText = @"
                        INSERT INTO UserSettings (TrackPrivateMode, TrackVPN, BreakTime, NotifyBreakEveryTime, ActivityTrackingScope)
                        SELECT 1, 1, '00:00', 0, 0
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
            int appIconId = -1;
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
                        INSERT INTO AppIcons(AppName, AppIcon)
                        VALUES (@AppName, @AppIcon)
                        ON CONFLICT(AppName) DO UPDATE SET AppIcon = excluded.AppIcon;";
                    cmd.Parameters.AddWithValue("@AppName", appName);
                    cmd.Parameters.AddWithValue("@AppIcon", (object)iconBytes ?? DBNull.Value);

                    await cmd.ExecuteNonQueryAsync();
                }

                // Retrive AppIconId from AppIcons table
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT AppIconId FROM AppIcons WHERE AppName = @AppName;";
                    cmd.Parameters.AddWithValue("@AppName", appName);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && int.TryParse(result.ToString(), out int id))
                    {
                        appIconId = Convert.ToInt32(result);
                    }
                }



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
                    cmd.Parameters.AddWithValue("@AppIcon", appIconId);
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
                 // General private mode keywords
                "inprivate",
                "incognito",
                "private browsing",
                "privacy mode",
                "stealth mode",
                "secret mode",

                // Chrome
                "incognito - google chrome",
                "new incognito tab",
                "incognito tab",
                "you’ve gone incognito",

                // Edge
                "inprivate browsing",
                "new inprivate tab",
                "inprivate window",
                "you’re browsing in inprivate",

                // Firefox
                "private browsing - mozilla firefox",
                "new private window",
                "private tab",
                "firefox private browsing",

                // Safari (macOS + iOS)
                "private browsing - safari",
                "new private tab",
                "safari private",
                "this is a private browsing window",

                // Opera
                "private browsing - opera",
                "new private window",
                "opera private",

                // Brave
                "private browsing - brave",
                "new private tab",
                "brave private",

                // Samsung Internet
                "secret mode - samsung internet",
                "new secret tab",
                "samsung internet secret",

                // Generic tab messages (commonly seen when opening a private window/tab)
                "you’re browsing privately",
                "you’ve gone incognito",
                "private tab",
                "new private tab",
                "secret tab",
                "new secret tab",
                // General adult terms
                "porn", "xxx", "sex", "nude", "adult", "cam", "escort",
                "hentai", "nsfw", "red light", "explicit",

                // Popular categories often in titles/domains
                "milf", "teen", "gay", "lesbian", "anal", "fetish", "bdsm",

                // Common adult site brand names (examples, not full list)
                "xvideos", "xhamster", "pornhub", "xnxx", "onlyfans",
                "redtube", "youporn", "brazzers", "bangbros", "chaturbate"
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
                            windowTitle.IndexOf(k, StringComparison.OrdinalIgnoreCase) > 0))
                            {
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


        private static readonly Dictionary<string, (string FriendlyName, string IconPath)> AppIcons = new Dictionary<string, (string FriendlyName, string IconPath)>()
        {
            { "ApplicationFrameHost", ("ApplicationFrameHost", "Assets/Icons/ApplicationFramHost2.png") },
            { "WhatsApp", ("WhatsApp", "Assets/Icons/WhatsApp.png") },
            { "WhatsApp.Root", ("WhatsApp", "Assets/Icons/WhatsApp.png") },
            { "Spotify", ("Spotify", "Assets/Icons/spotify.png") },
            { "Microsoft Teams", ("Microsoft Teams", "Assets/Icons/teams.png") },
            { "Telegram", ("Telegram", "Assets/Icons/telegram.png") },
            { "Mail", ("Mail", "Assets/Icons/mail.png") },
            { "Calendar", ("Calendar", "Assets/Icons/calendar.png") },
            { "Photos", ("Photos", "Assets/Icons/photos.png") },
            { "Xbox", ("Xbox", "Assets/Icons/xbox.png") },
            { "Groove Music", ("Groove Music", "Assets/Icons/groove.png") },
            { "Movies & TV", ("Movies & TV", "Assets/Icons/movies.png") },
            { "Sticky Notes", ("Sticky Notes", "Assets/Icons/stickynotes.png") },
            { "OneNote", ("OneNote", "Assets/Icons/onenote.png") },
            { "Skype", ("Skype", "Assets/Icons/skype.png") },
            { "Microsoft Store", ("Microsoft Store", "Assets/Icons/store.png") },
            { "Weather", ("Weather", "Assets/Icons/weather.png") },
            { "Maps", ("Maps", "Assets/Icons/maps.png") },
            { "Alarms & Clock", ("Alarms & Clock", "Assets/Icons/clock.png") },
            { "Desktop", ("Desktop", "Assets/Icons/desktop.png") }
        };
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
            var startTimer = System.Diagnostics.Stopwatch.StartNew();
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
                            SELECT u.AppName, u.ExePath, i.AppIcon, u.WindowTitle, u.StartTime, u.EndTime
                            FROM AppUsage u   
                            LEFT JOIN AppIcons i ON u.AppIcon = i.AppIconId
                            WHERE (@start IS NULL OR u.EndTime >= @start)
                            AND (@end IS NULL OR u.StartTime <= @end)
                            ORDER BY u.StartTime;
                        ";

                        cmd.Parameters.AddWithValue("@start", (object)start ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@end", (object)end ?? DBNull.Value);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            int startTimeIdx = reader.GetOrdinal("StartTime");
                            int endTimeIdx = reader.GetOrdinal("EndTime");

                            while (await reader.ReadAsync())
                            {
                                var startTime = reader.GetDateTime(startTimeIdx);
                                var endTime = reader.GetDateTime(endTimeIdx);

                                if (endTime > startTime)
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
                var filteredData = splitData.AsEnumerable();
                if (start.HasValue) filteredData = filteredData.Where(x => x.Date >= start.Value.Date).ToList();
                if (end.HasValue) filteredData = filteredData.Where(x => x.Date <= end.Value.Date).ToList();

                // 4️⃣ Determine if single-day or multi-day grouping
                // Determine if single-day or multi-day grouping
                IEnumerable<IGrouping<object, AppUsage>> groupedData;

                bool isSingleDayRange = start.HasValue && end.HasValue && start.Value.Date == end.Value.Date;

                if (isSingleDayRange)
                {
                    groupedData = filteredData.GroupBy(x => (object)new { x.AppName, x.Date });
                }
                else
                {
                    groupedData = filteredData.GroupBy(x => (object)x.AppName);
                }

                // Now build final result
                var result = groupedData.Select(g =>
                {
                    var newestRecord = g.OrderByDescending(x => x.Date).ThenByDescending(x => x.Duration).First();

                    string friendlyAppName = AppFriendlyNames.ContainsKey(newestRecord.AppName)
                        ? AppFriendlyNames[newestRecord.AppName]
                        : newestRecord.AppName;

                    // If AppIcon is null, try to get it from AppIcons dictionary
                    byte[] appIcon = newestRecord.AppIcon;
                    if ((appIcon == null || appIcon.Length == 0) && AppIcons.TryGetValue(newestRecord.AppName, out var iconInfo))
                    {
                        try
                        {

                            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            string iconPath = Path.Combine(baseDir, iconInfo.IconPath);


                            if (File.Exists(iconPath))
                            {
                                appIcon = File.ReadAllBytes(iconPath);
                            }
                        }
                        catch
                        {
                            appIcon = null; // fallback if reading fails
                        }
                    }

                    var totalDuration = TimeSpan.FromSeconds(g.Sum(x => x.Duration.TotalSeconds));

                    DateTime dateValue = isSingleDayRange
                        ? ((dynamic)g.Key).Date
                        : g.Max(x => x.Date);

                    return new AppUsage
                    {
                        AppName = friendlyAppName,
                        ExePath = newestRecord.ExePath,
                        AppIcon = appIcon,
                        Date = dateValue,
                        Duration = totalDuration
                    };
                })
                .OrderByDescending(x => x.Duration)
                .ToList();

                startTimer.Stop();
                //System.Diagnostics.Debug.WriteLine($"✅ GetAllAppUsageAsync executed in {startTimer.ElapsedMilliseconds} ms");

                return result;
            }
            catch (Exception ex)
            {
                startTimer.Stop();
                //System.Diagnostics.Debug.WriteLine($"GetAllAppUsageAsync failed after {startTimer.ElapsedMilliseconds} ms: {ex.Message}");
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
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var rawData = new List<AppUsage>();
            try
            {
                // Default to whole day if start/end not provided
                if (!start.HasValue) start = date.Date;
                if (!end.HasValue) end = date.Date.AddDays(1).AddTicks(-1);

                using (var conn = new SQLiteConnection(ConnString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {


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


                // 2. Get user settings ONCE (not inside the loop)
                var userSettingList = await GetUserSettings();
                bool trackPrivateMode = userSettingList.FirstOrDefault()?.TrackPrivateMode ?? false;

                // 3. Cache BlockedKeywords to avoid repeated ToLower() calls
                var blockedKeywords = trackPrivateMode ? Array.Empty<string>() :
                    TrackingFilters.BlockedKeywords.Select(k => k.ToLowerInvariant()).ToArray();

                // 4. Single-pass optimized filtering + splitting
                var splitData = new List<AppUsage>(rawData.Count);

                // Split sessions across days
                foreach (var session in rawData)
                {
                    if (!trackPrivateMode)
                    {
                        string title = session.WindowTitle?.ToLowerInvariant() ?? "";
                        if (blockedKeywords.Any(k => title.Contains(k)))
                            continue;
                    }

                    // Avoid unnecessary loops — most sessions are within a single day
                    if (session.StartTime.Date == session.EndTime.Date)
                    {
                        splitData.Add(new AppUsage
                        {
                            WindowTitle = session.WindowTitle,
                            StartTime = session.StartTime,
                            EndTime = session.EndTime,
                            Duration = session.EndTime - session.StartTime
                        });
                    }
                    else
                    {
                        // Split across two days only if needed
                        var dayEnd = session.StartTime.Date.AddDays(1);
                        var firstSegment = new AppUsage
                        {
                            WindowTitle = session.WindowTitle,
                            StartTime = session.StartTime,
                            EndTime = dayEnd,
                            Duration = dayEnd - session.StartTime
                        };

                        var secondSegment = new AppUsage
                        {
                            WindowTitle = session.WindowTitle,
                            StartTime = dayEnd,
                            EndTime = session.EndTime,
                            Duration = session.EndTime - dayEnd
                        };

                        splitData.Add(firstSegment);
                        splitData.Add(secondSegment);
                    }
                }

                // 5. Manual aggregation (O(n) instead of LINQ GroupBy)
                var grouped = new Dictionary<string, (DateTime minStart, DateTime maxEnd, double totalSeconds)>();
                foreach (var s in splitData)
                {
                    if (s.EndTime <= start.Value || s.StartTime >= end.Value)
                        continue;

                    if (grouped.TryGetValue(s.WindowTitle, out var agg))
                    {
                        agg.minStart = agg.minStart < s.StartTime ? agg.minStart : s.StartTime;
                        agg.maxEnd = agg.maxEnd > s.EndTime ? agg.maxEnd : s.EndTime;
                        agg.totalSeconds += s.Duration.TotalSeconds;
                        grouped[s.WindowTitle] = agg;
                    }
                    else
                    {
                        grouped[s.WindowTitle] = (s.StartTime, s.EndTime, s.Duration.TotalSeconds);
                    }
                }

                var groupedData = grouped
                .Select(g => new AppUsage
                {
                    WindowTitle = g.Key,
                    StartTime = g.Value.minStart,
                    EndTime = g.Value.maxEnd,
                    Duration = TimeSpan.FromSeconds(g.Value.totalSeconds)
                })
                .OrderByDescending(x => x.Duration)
                .ToList();

                stopwatch.Stop();
                //System.Diagnostics.Debug.WriteLine($"✅ GetAppDetailResultsByAppName executed in {stopwatch.ElapsedMilliseconds} ms (optimized)");
                return groupedData;

            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                //System.Diagnostics.Debug.WriteLine($"GetAppDetailResultsByAppName failed after {stopwatch.ElapsedMilliseconds} ms: {ex.Message}");
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

                // Check if BreakTime column exists
                bool breakTimeExists = false;
                bool NotifyBreakEveryTimeExists = false;
                bool ActivityTrackingScopeExists = false;
                using (var checkCmd = conn.CreateCommand())
                {
                    checkCmd.CommandText = "PRAGMA table_info(UserSettings);";
                    using (var reader = await checkCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string columnName = reader["name"].ToString();
                            if (columnName.Equals("BreakTime", StringComparison.OrdinalIgnoreCase))
                            {
                                breakTimeExists = true;
                            }
                            if (columnName.Equals("NotifyBreakEveryTime", StringComparison.OrdinalIgnoreCase))
                            {
                                NotifyBreakEveryTimeExists = true;
                            }
                            if(columnName.Equals("ActivityTrackingScope", StringComparison.OrdinalIgnoreCase)){
                                ActivityTrackingScopeExists = true;
                            }
                        }
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    if (breakTimeExists && NotifyBreakEveryTimeExists && ActivityTrackingScopeExists)
                        cmd.CommandText = "SELECT Id, TrackPrivateMode, TrackVPN, BreakTime, NotifyBreakEveryTime, ActivityTrackingScope FROM UserSettings LIMIT 1";
                    else if (breakTimeExists && NotifyBreakEveryTimeExists)
                        cmd.CommandText = "SELECT Id, TrackPrivateMode, TrackVPN, BreakTime, NotifyBreakEveryTime FROM UserSettings LIMIT 1";
                    else if (breakTimeExists)
                        cmd.CommandText = "SELECT Id, TrackPrivateMode, TrackVPN, BreakTime FROM UserSettings LIMIT 1";
                    else
                        cmd.CommandText = "SELECT Id, TrackPrivateMode, TrackVPN FROM UserSettings LIMIT 1";



                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            rawSettings.Add(new UserSettings
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                TrackPrivateMode = Convert.ToBoolean(reader["TrackPrivateMode"]),
                                TrackVPN = Convert.ToBoolean(reader["TrackVPN"]),
                                BreakTime = breakTimeExists ? Convert.ToString(reader["BreakTime"]) : "00:00",
                                NotifyBreakEveryTime = NotifyBreakEveryTimeExists ? Convert.ToBoolean(reader["NotifyBreakEveryTime"]) : false,
                                ActivityTrackingScope = ActivityTrackingScopeExists ? Convert.ToBoolean(reader["ActivityTrackingScope"]) : false
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
                SELECT u.AppName, u.StartTime, u.EndTime, u.DurationSeconds, i.AppIcon
                FROM AppUsage u
                LEFT JOIN AppIcons i ON u.AppIcon = i.AppIconId
                WHERE u.EndTime >= @start AND u.StartTime <= @end
                      AND DurationSeconds > 0
                ORDER BY u.AppName, u.StartTime
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
                        string friendlyAppName = AppFriendlyNames.ContainsKey(row.AppName)
                           ? AppFriendlyNames[row.AppName]
                           : row.AppName;

                        byte[] appIcon = row.AppIcon;
                        if ((appIcon == null || appIcon.Length == 0) && AppIcons.TryGetValue(row.AppName, out var iconInfo))
                        {
                            try
                            {
                                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                                string iconPath = Path.Combine(baseDir, iconInfo.IconPath);

                                if (File.Exists(iconPath))
                                    appIcon = File.ReadAllBytes(iconPath);
                            }
                            catch
                            {
                                appIcon = null;
                            }
                        }

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
                                    AppIcon = appIcon
                                });
                        }

                        lastApp = row.AppName;
                        lastEnd = row.End;
                    }
                }
            }

            return counts.OrderByDescending(c => c.OpenCount).ToList();
        }

        public static async Task EnsureBreakTimeColumn()
        {
            try
            {
                using (var conn = new SQLiteConnection(Database.ConnString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        // Check if BreakTime column exists
                        cmd.CommandText = "PRAGMA table_info(UserSettings);";
                        var reader = await cmd.ExecuteReaderAsync();
                        bool columnExists = false;

                        while (await reader.ReadAsync())
                        {
                            string columnName = reader["name"].ToString();
                            if (columnName.Equals("BreakTime", StringComparison.OrdinalIgnoreCase))
                            {
                                columnExists = true;
                                break;
                            }
                        }
                        reader.Close();

                        // Add column if missing
                        if (!columnExists)
                        {
                            cmd.CommandText = "ALTER TABLE UserSettings ADD COLUMN BreakTime TEXT;";
                            await cmd.ExecuteNonQueryAsync();

                            // Initialize existing rows
                            cmd.CommandText = "UPDATE UserSettings SET BreakTime = '00:00' WHERE BreakTime IS NULL;";
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to ensure BreakTime column: " + ex.Message);
            }
        }
        public static async Task SaveBreakTimeToDatabase(string time)
        {
            try
            {
                using (var conn = new SQLiteConnection(Database.ConnString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE UserSettings SET BreakTime = @time WHERE Id = 1;";
                        cmd.Parameters.AddWithValue("@time", time);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to save BreakTime: " + ex.Message);
            }
        }

        public static async Task EnsureNotifyBreakEveryTimeColumn()
        {
            try
            {
                using (var conn = new SQLiteConnection(Database.ConnString))
                {
                    await conn.OpenAsync();

                    bool columnExists = false;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA table_info(UserSettings);";
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string columnName = reader["name"].ToString();
                                if (columnName.Equals("NotifyBreakEveryTime", StringComparison.OrdinalIgnoreCase))
                                {
                                    columnExists = true;
                                    break;
                                }
                            }
                        }
                    }

                    // Add the column if missing
                    if (!columnExists)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"ALTER TABLE UserSettings ADD COLUMN NotifyBreakEveryTime INTEGER DEFAULT 0;";
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("NotifyBreakEveryTime column already exists.");
                    }

                    // Ensure existing rows are initialized to 0
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE UserSettings SET NotifyBreakEveryTime = 0 WHERE NotifyBreakEveryTime IS NULL;";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to ensure NotifyBreakEveryTime column: " + ex.Message);
            }
        }
        public static async Task UpdateNotifyEveryTime(bool value)
        {
            using (var conn = new SQLiteConnection(ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE UserSettings SET NotifyBreakEveryTime = @value WHERE Id = 1;";
                    cmd.Parameters.AddWithValue("@value", value ? 1 : 0);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

        }


        public static async Task EnsureActivityTrackingScopeColumns()
        {
            try
            {
                using (var conn = new SQLiteConnection(Database.ConnString))
                {
                    await conn.OpenAsync();
                    bool columnExists = false;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA table_info(UserSettings);";
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string columnName = reader["name"].ToString();
                                if (columnName.Equals("ActivityTrackingScope", StringComparison.OrdinalIgnoreCase))
                                {
                                    columnExists = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!columnExists)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"ALTER TABLE UserSettings ADD COLUMN ActivityTrackingScope INTEGER DEFAULT 0;";
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ActivityTrackingScope column already exists.");
                    }

                    // Ensure existing rows are initialized to 0
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE UserSettings SET NotifyBreakEveryTime = 0 WHERE NotifyBreakEveryTime IS NULL;";
                        await cmd.ExecuteNonQueryAsync();
                    }

                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to ensure ActivityTrackingScope column: " + ex.Message);
            }
        }
        public static async Task<int> UpdateActivityTrackingScope(int value)
        {
            using (var conn = new SQLiteConnection(ConnString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE UserSettings SET ActivityTrackingScope = @value WHERE Id = 1;";
                    cmd.Parameters.AddWithValue("@value", value);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            return value;


        }


        private static CancellationTokenSource _cleanupCts;
        private static Task _cleanupTask;

        public static async Task CleanDuplicatesRecordAsync()
        {
            const string sql1 = @"
                DELETE FROM AppUsage
                WHERE ID IN (
                    SELECT b.ID
                    FROM AppUsage a
                    JOIN AppUsage b
                      ON a.AppName = b.AppName
                      AND a.WindowTitle = b.WindowTitle
                      AND IFNULL(a.ExePath, '') = IFNULL(b.ExePath, '')
                      AND ABS(strftime('%s', a.StartTime) - strftime('%s', b.StartTime)) <= 1
                      AND ABS(strftime('%s', a.EndTime) - strftime('%s', b.EndTime)) <= 1
                      AND ABS(a.DurationSeconds - b.DurationSeconds) <= 1
                      AND a.ID < b.ID
                    WHERE a.EndTime >= datetime('now', '-4 hour')
                );";

            const string sql2 = @"
            WITH Keepers AS (
                SELECT MAX(Id) AS IdToKeep
                FROM AppUsage
                WHERE AppName IS NOT NULL AND StartTime IS NOT NULL
                GROUP BY AppName, StartTime
            )
            DELETE FROM AppUsage
            WHERE Id NOT IN (SELECT IdToKeep FROM Keepers);
        ";

            try
            {
                using (var conn = new SQLiteConnection(ConnString))
                {
                    await conn.OpenAsync();

                    int totalDeleted = 0;

                    // Step 1: near-duplicate cleanup
                    using (var cmd1 = conn.CreateCommand())
                    {
                        cmd1.CommandText = sql1;
                        int affected1 = await cmd1.ExecuteNonQueryAsync();
                        totalDeleted += affected1;
                        //Debug.WriteLine($"🧹 Step 1: Deleted {affected1} recent duplicates.");
                    }

                    // Step 2: overlapping cleanup (safe version)
                    using (var cmd2 = conn.CreateCommand())
                    {
                        cmd2.CommandText = sql2;
                        int affected2 = await cmd2.ExecuteNonQueryAsync();
                        totalDeleted += affected2;
                        //Debug.WriteLine($"🧹 Step 2: Deleted {affected2} global duplicates.");
                    }

                    //Debug.WriteLine($"✅ Total {totalDeleted} records deleted at {DateTime.Now:HH:mm:ss}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("❌ CleanDuplicatesRecordAsync failed: " + ex.Message);
            }
        }
        public static void StartAutoCleanupLoop()
        {
            if (_cleanupTask != null && !_cleanupTask.IsCompleted)
                return;

            _cleanupCts = new CancellationTokenSource();
            _cleanupTask = Task.Run(() => CleanupLoopAsync(_cleanupCts.Token));
        }

        private static async Task CleanupLoopAsync(CancellationToken token)
        {
            //Debug.WriteLine("🕒 Auto-cleanup background task started.");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await CleanDuplicatesRecordAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[AutoCleanup] Error: " + ex.Message);
                }

                DateTime now = DateTime.Now;
                DateTime nextHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
                TimeSpan delay = nextHour - now;

                try
                {
                    await Task.Delay(delay, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            Debug.WriteLine("🛑 Auto-cleanup background task stopped.");
        }
        public static void StopAutoCleanupLoop()
        {
            if (_cleanupCts != null)
            {
                _cleanupCts.Cancel();
                _cleanupCts = null;
                _cleanupTask = null;
            }
        }



    }


}
