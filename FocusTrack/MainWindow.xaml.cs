using FocusTrack.Model;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
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
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static FocusTrack.Database;

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
            Database.Initialize();

            lastStart = DateTime.Now;

            timer = new System.Timers.Timer(5000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();

            this.Loaded += async (_, __) => await LoadDefaultGraph();
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
               
            });
        }








        private async void RangeSelectot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RangeSelecter.SelectedItem is ComboBoxItem selected)
            {
                string range = selected.Tag.ToString();

                List<HourlyUsage> data = null;

                switch (range)
                {
                    case "today":
                        data = await Database.GetHourlyUsageAsync(DateTime.Today, DateTime.Now);
                        break;

                    case "7h":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddHours(-7), DateTime.Now);
                        break;

                    case "24h":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddHours(-24), DateTime.Now);
                        break;

                    case "7d":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddDays(-7), DateTime.Now);
                        break;

                    case "1m":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddMonths(-1), DateTime.Now);
                        break;

                    case "3m":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddMonths(-3), DateTime.Now);
                        break;
                }
                if (data != null)
                {
                    LoadGraphData(data);
                }
            }
        }
        private void LoadGraphData(List<HourlyUsage> data)
        {
            if (UsageChart == null) return;
            UsageChart.Series = new ISeries[]
            {
                new ColumnSeries<int>
                {
                    Values = data.Select(d => d.TotalSeconds / 60).ToArray(), // minutes
                    Name = "Usage Time",
                    Fill = new SolidColorPaint(SKColors.DodgerBlue)
                }
            };

            UsageChart.XAxes = new[]
            {
                new Axis
                {
                    Labels = data.Select(d => d.Hour.ToString("00") + ":00").ToArray(),
                    Name = "Hour of Day"
                }
            };

            UsageChart.YAxes = new[]
            {
                new Axis
                {
                    Name = "Usage",
                    MinLimit = 0,
                    Labeler = value =>
                    {
                        if (value < 60)
                            return $"{value} min";
                        else
                            return $"{value / 60:0} hr";
                    }
                }
            };
        }
        
        
        private async Task LoadDefaultGraph()
        {
            var todayData = await Database.GetHourlyUsageAsync(DateTime.Today, DateTime.Now);
            LoadGraphData(todayData);
        }


        // For dragging the window
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
 
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
        
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
