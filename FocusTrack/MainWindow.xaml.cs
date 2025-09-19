using FocusTrack.Controls;
using FocusTrack.helpers;
using FocusTrack.Model;
using FocusTrack.Pages;
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


            MainFrame.Navigate(new HomePage());
            // Create tray icon
            SetupNotifyIcon();
            DataContext = this;

            AppUsages = new ObservableCollection<AppUsage>();

            // Ensure DB file & AppUsage table exist before anything else uses it
            Database.Initialize();
            // Load TrackPrivateMode from DB at startup
            InitializeTrackPrivateMode();
            lastStart = DateTime.Now;

            timer = new System.Timers.Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();

            this.DataContext = this;

            _ = LoadSettingsAtStartupAsync();

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

        private async Task RefreshUIAsync()
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (MainFrame.Content is HomePage homePage)
                {
                    // Refresh HomePage UI
                    SelectedDate = DateTime.Today;

                    var allData = await Database.GetAllAppUsageAsync(DateTime.Today, DateTime.Now);
                    var todayData = await Database.GetHourlyUsageAsync(DateTime.Today, DateTime.Now);

                    homePage.RangeSelecter.SelectedIndex = 0;
                    homePage.AppUsages.Clear();
                    foreach (var item in allData)
                        homePage.AppUsages.Add(item);

                    homePage.UpdateTotalUsage();
                    homePage.LoadGraphData(todayData);
                }
                else if (MainFrame.Content is AppOpenCountPage appOpenCountPage)
                {
                    // Refresh AppOpenCountPage UI
                    var start = SelectedDate.Date;
                    var end = SelectedDate.Date.AddDays(1).AddSeconds(-1);
                    var allData = await Database.GetAppOpenCountAsync(start, end);

                    appOpenCountPage.RangeSelecter.SelectedIndex = 0;
                    appOpenCountPage.AppUsages.Clear();
                    foreach (var item in allData)
                        appOpenCountPage.AppUsages.Add(item);
                }
            });
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

        // Async helper to load setting
        private async void InitializeTrackPrivateMode()
        {
            var settings = await Database.GetUserSettings();
            if (settings.Count > 0)
            {
                ActiveWindowTracker.TrackPrivateModeEnabled = settings[0].TrackPrivateMode;
                System.Diagnostics.Debug.WriteLine($"TrackPrivateMode loaded at startup: {settings[0].TrackPrivateMode}");
            }
        }


        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(MainFrame.Content is Settings)) { 
                MainFrame.Navigate(new Settings());
            }
        }
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(MainFrame.Content is HomePage)) { 
                MainFrame.Navigate(new HomePage());
            }
        }

        private void AppOpenTimeButton_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new AppOpenCountPage());
        }

        private async Task LoadSettingsAtStartupAsync()
        {
            var settingsList = await Database.GetUserSettings();
            if (settingsList.Count > 0)
            {
                var settings = settingsList[0];

                // Update tracker flags
                ActiveWindowTracker.TrackPrivateModeEnabled = settings.TrackPrivateMode;
                ActiveWindowTracker.TrackVPNEnabled = settings.TrackVPN;

            }
        }



    }
}
