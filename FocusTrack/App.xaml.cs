using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            //ActiveWindowTracker.InitializePowerEventHandlers();
            // Ensure DB + table exist before UI
            FocusTrack.Database.Initialize();
            // Start hourly background cleanup
            Database.StartAutoCleanupLoop();

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Stop background cleanup gracefully
            Database.StopAutoCleanupLoop();
            base.OnExit(e);
        }

    }
}
