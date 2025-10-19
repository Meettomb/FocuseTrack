using FocusTrack.Controls;
using FocusTrack.helpers;
using FocusTrack.model;
using FocusTrack.Model;
using FocusTrack.Pages;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
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
using System.Windows.Threading;
using static FocusTrack.Database;
using IOPath = System.IO.Path;
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
        private ManagementEventWatcher lidWatcher;
        private static DateTime lastWakeTime = DateTime.MinValue;
        public bool isSystemSleeping = false;
        private DateTime? sleepStartTime = null;

        private DispatcherTimer _timer;
        private TimeSpan _interval;
        private DateTime _nextNotifyTime;


        private System.Threading.Timer _settingsWatcherTimer;
        private string _lastBreakTime;
        private bool _lastNotifyBreakEveryTime;
        public Frame AppFrame => MainFrame;


        public WinForms.NotifyIcon notifyIcon;
        public ObservableCollection<AppUsage> AppUsages { get; set; }
        public UserSettings UserSettings { get; set; }
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


            // Refresh UI when window gains focus
            this.Activated += MainWindow_Activated;

            // Optionally, refresh when the window is first loaded
            this.ContentRendered += MainWindow_ContentRendered;

            timer = new System.Timers.Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();

            this.DataContext = this;

            _ = LoadSettingsAtStartupAsync();

            _ = LoadSettingsAndStartTimer(); // Start notifications immediately
            StartSettingsWatcher(); // Also start DB watcher for live updates


            // Subscribe to power mode changes
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            // Subscribe to session switch (lock/unlock)
            SystemEvents.SessionSwitch += OnSessionSwitch;
            StartLidWatcher();


        }

        private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (isSystemSleeping)
            {
                if (!string.IsNullOrEmpty(lastApp) && sleepStartTime.HasValue)
                {
                    await Database.SaveSessionAsync(lastApp, lastTitle, lastStart, sleepStartTime.Value, lastExePath);
                    Debug.WriteLine($"[SAVE] Session ended due to sleep at {sleepStartTime.Value}");
                }

                lastApp = null;
                lastTitle = null;
                lastStart = DateTime.Now;
                lastExePath = null;

                return; // Skip saving during sleep
            }



            var userSettingList = await GetUserSettings();
            var TrackPrivateMode = userSettingList[0].TrackPrivateMode;
            if (TrackPrivateMode == false)
            {

            }
            var active = ActiveWindowTracker.GetActiveWindowInfo();

            string appName = active.AppName;
            string windowTitle = active.Title;
            string exePath = active.ExePath;

            if (string.IsNullOrWhiteSpace(appName)) return;

            string myExeName = Process.GetCurrentProcess().MainModule.FileName;

            // Fetch user settings once per timer tick
            bool trackPrivateMode = userSettingList[0].TrackPrivateMode;
            bool isBlocked = !trackPrivateMode && Database.TrackingFilters.BlockedKeywords
                       .Any(k => windowTitle?.ToLowerInvariant().Contains(k) ?? false);
            // Skip tracking if blocked
            if (isBlocked) return;



            // Skip tracking for your own app
            if (string.Equals(exePath, myExeName, StringComparison.OrdinalIgnoreCase))
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

        private async Task RefreshUIAsync()
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (MainFrame.Content is HomePage homePage)
                {

                    var allData = await Database.GetAllAppUsageAsync(DateTime.Today, DateTime.Now);
                    var todayData = await Database.GetHourlyUsageAsync(DateTime.Today, DateTime.Now);

                    homePage.RangeSelecter.SelectedIndex = 0;
                    homePage.AppUsages.Clear();
                    foreach (var item in allData)
                        homePage.AppUsages.Add(item);

                    homePage.UpdateTotalUsage();
                    homePage.LoadGraphData(todayData);
                    homePage.SelectedDate = DateTime.Today;
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


                    appOpenCountPage.SelectedDate = DateTime.Today;
                }
            });
        }

        private async void MainWindow_Activated(object sender, EventArgs e)
        {
            await RefreshUIAsync();
        }

        private async void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            await RefreshUIAsync();
        }



        // For dragging the window
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            var settingsPage = MainFrame.Content as Settings;
            this.WindowState = WindowState.Minimized;
            if (settingsPage != null && settingsPage.TimePopup.IsOpen)
            {
                settingsPage.TimePopup.IsOpen = false;
            }
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            var settingsPage = MainFrame.Content as Settings;
            if (this.Tag == null || this.Tag.ToString() != "Maximized")
            {
                // Save restore size before maximizing
                this.Tag = "Maximized";
                this.Left = SystemParameters.WorkArea.Left;
                this.Top = SystemParameters.WorkArea.Top;
                this.Width = SystemParameters.WorkArea.Width;
                this.Height = SystemParameters.WorkArea.Height;
                if (settingsPage != null && settingsPage.TimePopup.IsOpen)
                {
                    settingsPage.TimePopup.IsOpen = false;
                }
            }
            else
            {
                // Restore
                this.Tag = "Normal";
                this.Width = 1000;
                this.Height = 700;
                this.Left = (SystemParameters.WorkArea.Width - this.Width) / 2;
                this.Top = (SystemParameters.WorkArea.Height - this.Height) / 2;
                if (settingsPage != null && settingsPage.TimePopup.IsOpen)
                {
                    settingsPage.TimePopup.IsOpen = false;
                }
            }
        }


        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            this.ShowInTaskbar = false;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Prevent the app from fully closing
            e.Cancel = true;

            // Hide the window
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

        public void SetupNotifyIcon()
        {
            notifyIcon = new WinForms.NotifyIcon();
            string iconPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "AppLogo", "FocusTrack.ico");
            notifyIcon.Icon = new System.Drawing.Icon(iconPath);

            notifyIcon.Visible = true;

            // Double-click on tray icon to show main window
            notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.ShowInTaskbar = true;
            };

            // Balloon tip click opens Settings page
            notifyIcon.BalloonTipClicked += (s, e) =>
            {
                // Use fully qualified WPF Application
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // Show main window
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    this.ShowInTaskbar = true;

                    // Navigate to Settings page
                    AppFrame.Navigate(new Pages.Settings());
                });
            };



            var menu = new WinForms.ContextMenuStrip();

            var openItem = new WinForms.ToolStripMenuItem("Open");
            openItem.Click += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.ShowInTaskbar = true;
            };

            var exitItem = new WinForms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                System.Windows.Application.Current.Shutdown(); // proper WPF shutdown
            };

            menu.Items.Add(openItem);
            menu.Items.Add(exitItem);

            notifyIcon.ContextMenuStrip = menu;
        }

        // Async helper to load setting
        private async void InitializeTrackPrivateMode()
        {
            var settings = await Database.GetUserSettings();
            if (settings.Count > 0)
            {
                ActiveWindowTracker.TrackPrivateModeEnabled = settings[0].TrackPrivateMode;
            }
        }


        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(MainFrame.Content is Settings))
            {
                MainFrame.Navigate(new Settings());
            }
        }
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(MainFrame.Content is HomePage))
            {
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






        private void StartSettingsWatcher()
        {
            _settingsWatcherTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    var settingsList = await Database.GetUserSettings();
                    if (settingsList.Count > 0)
                    {
                        var newSettings = settingsList[0];

                        if (newSettings.BreakTime != _lastBreakTime ||
                            newSettings.NotifyBreakEveryTime != _lastNotifyBreakEveryTime)
                        {
                            _lastBreakTime = newSettings.BreakTime;
                            _lastNotifyBreakEveryTime = newSettings.NotifyBreakEveryTime;

                            await RefreshSettingsAsync();

                            // Switch back to UI thread for timer + notification setup
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                _nextNotifyTime = DateTime.Now.Add(_interval);
                                StartTimer();
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Settings watcher error: " + ex.Message);
                }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10)); // check every 10s
        }


        public async System.Threading.Tasks.Task RefreshSettingsAsync()
        {
            var settingsList = await Database.GetUserSettings();
            if (settingsList.Count > 0)
            {
                UserSettings = settingsList[0];

                // Parse BreakTime
                if (!TimeSpan.TryParse(UserSettings.BreakTime, out _interval))
                {
                    _interval = TimeSpan.Zero; // invalid value → disable notifications
                }

                // Keep watcher in sync
                _lastBreakTime = UserSettings.BreakTime;
                _lastNotifyBreakEveryTime = UserSettings.NotifyBreakEveryTime;

                // Stop timer if interval is zero
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_interval.TotalMinutes <= 0)
                    {
                        _timer?.Stop();
                        _nextNotifyTime = DateTime.MaxValue; // never fires
                    }
                });
            }
        }


        private async System.Threading.Tasks.Task LoadSettingsAndStartTimer()
        {
            await RefreshSettingsAsync();
            _nextNotifyTime = DateTime.Now.Add(_interval);
            StartTimer();
        }

        private void StartTimer()
        {
            if (_timer != null)
                _timer.Stop();

            if (_interval.TotalMinutes <= 0)
                return;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (DateTime.Now >= _nextNotifyTime)
            {
                ShowNotification();

                if (UserSettings.NotifyBreakEveryTime)
                {
                    _nextNotifyTime = DateTime.Now.Add(_interval);
                }
                else
                {
                    _timer.Stop();
                }
            }
        }

        private void ShowNotification()
        {
            string soundPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds", "notification.wav");

            if (File.Exists(soundPath))
                new SoundPlayer(soundPath).Play();
            else
                SystemSounds.Beep.Play();

            var notification = new BreakNotificationWindow(soundPath);
            notification.Show();

            if (notifyIcon != null)
            {
                notifyIcon.BalloonTipTitle = "Break Time!";
                notifyIcon.BalloonTipText = "It's time to take a short break.";
                notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
                notifyIcon.ShowBalloonTip(5000);
            }

        }



        // To Detect Sleep/Wake and Lock/Unlock events
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    isSystemSleeping = true;
                    sleepStartTime = DateTime.Now;
                    Debug.WriteLine("System is going to sleep.");
                    break;

                case PowerModes.Resume:
                    isSystemSleeping = false;
                    sleepStartTime = null;
                    Debug.WriteLine("System has resumed from sleep.");
                    break;
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                isSystemSleeping = true;
                sleepStartTime = DateTime.Now;
                Debug.WriteLine($"[EVENT] Session locked at {DateTime.Now}");
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                isSystemSleeping = false;
                sleepStartTime = null;
                Debug.WriteLine($"[EVENT] Session unlocked at {DateTime.Now}");
            }
        }

        private void StartLidWatcher()
        {
            try
            {
                string query = "SELECT * FROM Win32_PowerManagementEvent";
                lidWatcher = new ManagementEventWatcher(new WqlEventQuery(query));
                lidWatcher.EventArrived += LidWatcher_EventArrived;
                lidWatcher.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error starting lid watcher: " + ex.Message);
            }
        }

        private void LidWatcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            ushort eventType = (ushort)e.NewEvent.Properties["EventType"].Value;

            if (eventType == 11)
            {
                isSystemSleeping = true;
                sleepStartTime = DateTime.Now;
            }
            else if (eventType == 12)
            {
                isSystemSleeping = false;
                sleepStartTime = null;
            }
        }


        protected override void OnClosed(EventArgs e)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            if (lidWatcher != null)
            {
                lidWatcher.Stop();
                lidWatcher.Dispose();
            }

            base.OnClosed(e);
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove(); // Allows dragging the window
            }
        }





    }
}
