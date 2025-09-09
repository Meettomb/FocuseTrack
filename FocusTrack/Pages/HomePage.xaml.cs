using FocusTrack.Controls;
using FocusTrack.helpers;
using FocusTrack.Model;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using static FocusTrack.Database;


using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore;

namespace FocusTrack.Pages
{
    /// <summary>
    /// Interaction logic for HomePage.xaml
    /// </summary>
    public partial class HomePage : Page, INotifyPropertyChanged
    {
        public ObservableCollection<AppUsage> AppUsages { get; set; }
        public ImageSource IconImage { get; set; }
        private System.Timers.Timer timer;  // avoid ambiguity
        private string lastApp = "";
        private string lastTitle = "";
        private string lastExePath = "";
        private DateTime lastStart;

        private DateTime _selectedDate = DateTime.Today;
        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (_selectedDate != value)
                {
                    _selectedDate = value;
                    OnPropertyChanged(nameof(SelectedDate));

                    // Call the method when SelectedDate changes

                    _ = LoadDataForSelectedDate();
                }
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public HomePage()
        {
            InitializeComponent();

            // Assuming the Border is the direct child of the Popup
            if (CalendarPopup.Child is Border border && border.Child is CustomCalendar calendar)
            {
                calendar.DateSelected += (s, date) =>
                {
                    CalendarPopup.IsOpen = false; // close the popup
                };
            }

            DataContext = this;

            AppUsages = new ObservableCollection<AppUsage>();

            // 🔹 Ensure DB file & AppUsage table exist before anything else uses it
            Database.Initialize();

            lastStart = DateTime.Now;

            timer = new System.Timers.Timer(5000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();

            this.DataContext = this;

            this.Loaded += async (_, __) =>
            {
                await LoadDefaultGraph();    // load chart
                await LoadDefaultAppUsage(); // load grid data safely
            };

            StartupHelper.AddToStartup();

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
                if (!string.IsNullOrEmpty(lastApp))
                {
                    await Database.SaveSessionAsync(lastApp, lastTitle, lastStart, DateTime.Now, lastExePath);
                    await RefreshUIAsync();
                }

                lastApp = null;
                lastTitle = null;
                lastStart = DateTime.Now;
                lastExePath = null;
                return;
            }

            if (appName != lastApp || windowTitle != lastTitle)
            {
                if (!string.IsNullOrEmpty(lastApp))
                {
                    await Database.SaveSessionAsync(lastApp, lastTitle, lastStart, DateTime.Now, lastExePath);
                    await RefreshUIAsync();
                }

                lastApp = appName;
                lastTitle = windowTitle;
                lastStart = DateTime.Now;
                lastExePath = exePath;
            }
        }

        // Helper to update grid and chart
        private async Task RefreshUIAsync()
        {
            SelectedDate = DateTime.Today;
            var allData = await Database.GetAllAppUsageAsync(DateTime.Today, DateTime.Now);
            var todayData = await Database.GetHourlyUsageAsync(DateTime.Today, DateTime.Now);

            Dispatcher.Invoke(() =>
            {
                AppUsages.Clear();
                foreach (var item in allData)
                    AppUsages.Add(item);

                LoadGraphData(todayData);
            });
        }


        private async void RangeSelectot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RangeSelecter.SelectedItem is ComboBoxItem selected)
            {
                string range = selected.Tag.ToString();

                List<HourlyUsage> data = null;
                List<HourlyUsage> Listdata = null;

                switch (range)
                {
                    case "today":
                        data = await Database.GetHourlyUsageAsync(DateTime.Today, DateTime.Now);
                        await LoadAllAppUsageAsync(DateTime.Today, DateTime.Now);
                        break;

                    case "7d":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddDays(-7), DateTime.Now);
                        await LoadAllAppUsageAsync(DateTime.Now.AddDays(-7), DateTime.Now);
                        break;

                    case "1m":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddMonths(-1), DateTime.Now);
                        await LoadAllAppUsageAsync(DateTime.Now.AddMonths(-1), DateTime.Now);
                        break;

                    case "3m":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddMonths(-3), DateTime.Now);
                        await LoadAllAppUsageAsync(DateTime.Now.AddMonths(-3), DateTime.Now);
                        break;

                    case "overall":
                        data = await Database.GetHourlyUsageAsync(DateTime.MinValue, DateTime.Now);
                        await LoadAllAppUsageAsync(null, null);
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

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LoadGraphData(data));
                return;
            }

            // Aggregate usage by hour of day across all dates
            var completeData = Enumerable.Range(0, 24)
                .Select(hour => new HourlyUsage
                {
                    Hour = hour,
                    TotalSeconds = data.Where(d => d.Hour == hour).Sum(d => d.TotalSeconds)
                })
                .ToList();

            UsageChart.Series = new ISeries[]
            {
            new ColumnSeries<double>
            {
                Values = completeData.Select(d => d.TotalSeconds / 60.0).ToArray(), // Total minutes
                Name = "Usage Time",
                Fill = new SolidColorPaint(SKColors.LightBlue),
                MaxBarWidth = 80 // wider bars for visual clarity

            }
            };


            UsageChart.XAxes = new[]
            {
                new Axis
                {
                    Labels = completeData.Select(d => d.Hour.ToString("00") + ":00").ToArray(),
                    LabelsRotation = 0,
                    Name = "Hour of Day",
                    LabelsPaint = new SolidColorPaint(SKColors.DodgerBlue),
                    NamePaint = new SolidColorPaint(SKColors.White),
                    NameTextSize = 14
                }
            };

            UsageChart.YAxes = new[]
            {
                new Axis
                {
                    Name = "Usage",
                    NamePaint = new SolidColorPaint(SKColors.White),
                    NameTextSize = 14,
                    LabelsPaint = new SolidColorPaint(SKColors.DodgerBlue),
                    MinLimit = 0,
                    Labeler = value =>
                    {
                        if (value < 1)
                            return $"{value * 60:0}s";
                        else if (value < 60)
                            return $"{value:0} min";
                        else
                        {
                            int hours = (int)(value / 60);
                            int minutes = (int)(value % 60);
                            return $"{hours}:{minutes:00} hr"; // e.g. 1:30 hr
                        }
                    }

                }
            };
        }








        private async Task LoadDefaultGraph()
        {
            var todayData = await Database.GetHourlyUsageAsync(DateTime.Today, DateTime.Now);
            LoadGraphData(todayData);
        }



        // Get all AppUsage data
        private async Task LoadAllAppUsageAsync(DateTime? start, DateTime? end)
        {
            var allData = await Database.GetAllAppUsageAsync(start, end) ?? new List<AppUsage>();

            Dispatcher.Invoke(() =>
            {
                if (AppUsages == null)
                    AppUsages = new ObservableCollection<AppUsage>();

                AppUsages.Clear();
                foreach (var item in allData)
                    AppUsages.Add(item);

            });
        }

        private async Task LoadDefaultAppUsage()
        {
            await LoadAllAppUsageAsync(DateTime.Today, DateTime.Now);
        }

        // Previous Day Button
        private void PrevDayButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedDate = SelectedDate.AddDays(-1);
        }

        private void NextDayButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDate < DateTime.Today)
                SelectedDate = SelectedDate.AddDays(1);
        }

        private void DateTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CalendarPopup.IsOpen = true;
        }

        private async Task LoadDataForSelectedDate()
        {
            DateTime start = SelectedDate.Date;
            DateTime end = SelectedDate.Date.AddDays(1).AddSeconds(-1);

            var hourlyData = await Database.GetHourlyUsageAsync(start, end);
            LoadGraphData(hourlyData);

            await LoadAllAppUsageAsync(start, end);
        }




    }
}
