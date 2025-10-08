using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;

namespace FocusTrack
{
    public class BackgroundTracker
    {
        private Timer timer;
        private string lastApp = "";
        private string lastTitle = "";
        private string lastExePath = "";
        private DateTime lastStart;
        private readonly string myExePath;

        public BackgroundTracker()
        {
            // Use Assembly location instead of Process.MainModule.FileName
            // This works even if app is installed with an installer
            myExePath = Assembly.GetExecutingAssembly().Location;
        }

        public void Start()
        {
            lastStart = DateTime.Now;

            timer = new Timer(5000); // check every 5 seconds
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();
        }

        private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                var active = ActiveWindowTracker.GetActiveWindowInfo();
                if (string.IsNullOrWhiteSpace(active.AppName) ||
                    string.IsNullOrWhiteSpace(active.Title) ||
                    string.IsNullOrWhiteSpace(active.ExePath))
                {
                    return;
                }


                string appName = active.AppName;
                string windowTitle = active.Title;
                string exePath = active.ExePath;

                if (string.IsNullOrWhiteSpace(appName)) return;

                // Ignore own application
                if (string.Equals(exePath, myExePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(lastApp))
                    {
                        await Database.SaveSessionAsync(lastApp, lastTitle, lastStart, DateTime.Now, lastExePath);
                    }

                    lastApp = null;
                    lastTitle = null;
                    lastStart = DateTime.Now;
                    lastExePath = null;
                    return;
                }

                // Detect new app or window title change
                if (appName != lastApp || windowTitle != lastTitle)
                {
                    if (!string.IsNullOrEmpty(lastApp))
                    {
                        await Database.SaveSessionAsync(lastApp, lastTitle, lastStart, DateTime.Now, lastExePath);
                    }

                    lastApp = appName;
                    lastTitle = windowTitle;
                    lastStart = DateTime.Now;
                    lastExePath = exePath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("BackgroundTracker error: " + ex.Message);
            }
        }

        public void Stop()
        {
            try
            {
                timer?.Stop();
                timer?.Dispose();

                // Save last session on stop
                if (!string.IsNullOrEmpty(lastApp))
                {
                    Database.SaveSessionAsync(lastApp, lastTitle, lastStart, DateTime.Now, lastExePath).Wait();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("BackgroundTracker Stop error: " + ex.Message);
            }
        }
    }
}
