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
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms; // for NotifyIcon
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static FocusTrack.Database;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms;


namespace FocusTrack
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        private WinForms.NotifyIcon notifyIcon;
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
                }
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public MainWindow()
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

            // Create tray icon
            SetupNotifyIcon();
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

                    case "24h":
                        data = await Database.GetHourlyUsageAsync(DateTime.Now.AddHours(-24), DateTime.Now);
                        await LoadAllAppUsageAsync(DateTime.Now.AddHours(-24), DateTime.Now);
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
                        data = await Database.GetHourlyUsageAsync(null, null);
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

            // Use double for fractional minutes
            UsageChart.Series = new ISeries[]
            {
            new ColumnSeries<double>
            {
                Values = data.Select(d => d.TotalSeconds / 60.0).ToArray(), // convert seconds to minutes
                Name = "Usage Time",
                Fill = new SolidColorPaint(SKColors.LightBlue)
            }
            };

            UsageChart.XAxes = new[]
            {
                new Axis
                {
                    Labels = data.Select(d => d.Hour.ToString("00") + ":00").ToArray(),
                    Name = "Hour of Day",

                    LabelsPaint = new SolidColorPaint(SKColors.DodgerBlue),
                    // Color of the axis title
                    NamePaint = new SolidColorPaint(SKColors.White)
                    {

                    },
                }
            };

            UsageChart.YAxes = new[]
            {
                new Axis
                {
                    Name = "Usage",
                    NamePaint = new SolidColorPaint(SKColors.White)
                    {

                    },
                    LabelsPaint = new SolidColorPaint(SKColors.DodgerBlue),
                    MinLimit = 0,
                    Labeler = value =>
                    {
                        if (value < 1)
                            return $"{value * 60:0}s";   // show seconds if less than 1 minute
                        else if (value < 60)
                            return $"{value:0} min";      // show minutes
                        else
                            return $"{value / 60:0} hr"; // show hours if more than 60 min
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
                {
                    AppUsages.Add(item);
                }

                if (AppUsageGrid != null)
                    AppUsageGrid.ItemsSource = AppUsages;
            });
        }

        private async Task LoadDefaultAppUsage()
        { 
           await LoadAllAppUsageAsync(DateTime.Today, DateTime.Now);
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
            this.Hide();
            this.ShowInTaskbar = false;
        }

        private void ExitApplication()
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            System.Windows.Application.Current.Shutdown(); // WPF shutdown
        }



        private void ShowWindow()
        {
            this.Dispatcher.Invoke(() =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.ShowInTaskbar = true;
                this.Activate();
            });
        }

      
        private void Setting_Button(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings button clicked!");
        }

        private void SetupNotifyIcon()
        {
            notifyIcon = new WinForms.NotifyIcon();
            notifyIcon.Icon = new System.Drawing.Icon("D:\\Website\\Dot Net Project\\FocusTrack\\FocusTrack\\Images\\AppLogo\\FocusTrack.ico");
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.ShowInTaskbar = true;
            };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Open", null, (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.ShowInTaskbar = true;
            });

            // Call WPF shutdown instead of WinForms Application.Exit()
            menu.Items.Add("Exit", null, (s, e) =>
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                System.Windows.Application.Current.Shutdown(); // proper WPF shutdown
            });

            notifyIcon.ContextMenuStrip = menu;
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
    }
}
