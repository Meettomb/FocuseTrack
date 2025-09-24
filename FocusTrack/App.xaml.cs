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

            // Ensure DB + table exist before UI
            FocusTrack.Database.Initialize();

            // Register the app to run on Windows startup (default ON)
            FocusTrack.helpers.StartupHelper.AddToStartup();

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

    }
}
