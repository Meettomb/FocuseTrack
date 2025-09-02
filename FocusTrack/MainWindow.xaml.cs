using FocusTrack.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FocusTrack
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<AppUsage> AppUsages { get; set; }
        public ImageSource IconImage { get; set; }
        private System.Timers.Timer timer;  // avoid ambiguity
        private string lastApp = "";
        private string lastTitle = "";
        private string lastExePath = "";
        private DateTime lastStart;


        public MainWindow()
        {
            InitializeComponent();
            AppUsages = new ObservableCollection<AppUsage>();
            Database.Initialize(); // Make sure DB is ready

            lastStart = DateTime.Now;

            timer = new System.Timers.Timer(5000); // check every 5 seconds
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();

            LoadData();
        }

        private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var active = ActiveWindowTracker.GetActiveWindowInfo();
            string appName = active.AppName;
            string windowTitle = active.Title;
            string exePath = active.ExePath;

            if (string.IsNullOrWhiteSpace(appName)) return;

            string myExeName = Process.GetCurrentProcess().MainModule.FileName;

            if (string.Equals(exePath, myExeName, StringComparison.OrdinalIgnoreCase))
            {
                // ✅ Current app is FocusTrack → save previous app immediately
                if (!string.IsNullOrEmpty(lastApp))
                {
                    await Database.SaveSessionAsync(lastApp, lastTitle, lastStart, DateTime.Now, lastExePath);
                }

                // Reset last app tracking because we don't track FocusTrack
                lastApp = null;
                lastTitle = null;
                lastStart = DateTime.Now;
                lastExePath = null;
                return;
            }

            // If app changed or title changed
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

            Dispatcher.Invoke(() =>
            {
                LoadData();
            });
        }




        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        private async void LoadData()
        {
            var data = await Database.GetAllDataAsync(); // returns List<AppUsage>
            Dispatcher.Invoke(() =>
            {
                //dgUsage.ItemsSource = data; // bind list directly
            });
        }

        // For the Minimize button
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // Maximize / Restore window
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.Tag == null || this.Tag.ToString() != "Maximized")
            {
                // Save restore size before maximizing
                this.Tag = "Maximized";
                this.Left = SystemParameters.WorkArea.Left;
                this.Top = SystemParameters.WorkArea.Top;
                this.Width = SystemParameters.WorkArea.Width;
                this.Height = SystemParameters.WorkArea.Height;
            }
            else
            {
                // Restore
                this.Tag = "Normal";
                this.Width = 1000;
                this.Height = 700;
                this.Left = (SystemParameters.WorkArea.Width - this.Width) / 2;
                this.Top = (SystemParameters.WorkArea.Height - this.Height) / 2;
            }
        }




        // Close window
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Setting_Button(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings button clicked!");
        }



    }
}
