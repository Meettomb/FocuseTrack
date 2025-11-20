using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FocusTrack
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure DB + table exist before UI
            Database.Initialize();

            // Start hourly background cleanup
            Database.StartAutoCleanupLoop();

            // Load UI
            var mainWindow = new MainWindow();
            mainWindow.Show();

            try
            {
                var settings = (await Database.GetUserSettings()).FirstOrDefault();
                if (settings != null)
                {
                    // Check if cleanup is needed
                    if (!DateTime.TryParse(settings.LastCleanupDate, out DateTime lastCleanup) ||
                        lastCleanup.Date < DateTime.Now.Date) // Only run if a NEW day
                    {
                        await Database.CleanHistoryAccordingToRetentionOncePerDay();
                    }
                    else
                    {
                        Debug.WriteLine($"🛑 Cleanup skipped. Already executed today at {lastCleanup:yyyy-MM-dd}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("⚠ Error during cleanup call from App.xaml.cs → " + ex.Message);
            }
        }


        protected override void OnExit(ExitEventArgs e)
        {
            // Stop background cleanup gracefully
            Database.StopAutoCleanupLoop();
            base.OnExit(e);
        }

    }
}
