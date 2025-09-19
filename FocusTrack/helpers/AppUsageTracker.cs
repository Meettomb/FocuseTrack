using System;
using System.Diagnostics;
using System.Timers;
using System.Threading.Tasks;
using FocusTrack.Model;
using static FocusTrack.Database;

namespace FocusTrack.Helpers
{
    public class AppUsageTracker
    {
        private static AppUsageTracker _instance;
        private static readonly object _lock = new object();

        // Singleton instance
        public static AppUsageTracker Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AppUsageTracker();
                        }
                    }
                }
                return _instance;
            }
        }

        private (string AppName, string Title, string ExePath, byte[] AppIcon) lastActive;
        private DateTime lastStartTime;

        private readonly System.Timers.Timer timer;

        private AppUsageTracker()
        {
            timer = new System.Timers.Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var active = ActiveWindowTracker.GetActiveWindowInfo();

            if (string.IsNullOrWhiteSpace(active.AppName) || string.IsNullOrWhiteSpace(active.ExePath))
                return;

            // Ignore your own tool
            string myExe = Process.GetCurrentProcess().MainModule.FileName;
            if (string.Equals(active.ExePath, myExe, StringComparison.OrdinalIgnoreCase))
            {
                lastActive = active;
                lastStartTime = DateTime.Now;
                return;
            }

            // If app changed
            if (active.ExePath != lastActive.ExePath)
            {
                // Save previous session
                if (!string.IsNullOrEmpty(lastActive.ExePath))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Database.SaveSessionAsync(
                                lastActive.AppName,
                                lastActive.Title,
                                lastStartTime,
                                DateTime.Now,
                                lastActive.ExePath
                            );
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                        }
                    });
                }

                lastActive = active;
                lastStartTime = DateTime.Now;
            }
        }
    }
}
