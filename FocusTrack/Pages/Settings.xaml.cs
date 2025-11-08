using FocusTrack.Controls;
using FocusTrack.helpers;
using FocusTrack.Helpers;
using FocusTrack.model;
using FocusTrack.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace FocusTrack.Pages
{

    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : Page
    {
        private DispatcherTimer _timer;
        private TimeSpan _interval;
        private DateTime _nextNotifyTime;


        private string _targetTime;
        private bool _isPrivateModeAlertOpen;
        public bool IsPrivateModeAlertOpen
        {
            get => _isPrivateModeAlertOpen;
            set
            {
                if (_isPrivateModeAlertOpen != value)
                {
                    _isPrivateModeAlertOpen = value;
                    OnPropertyChanged(nameof(IsPrivateModeAlertOpen));
                }
            }
        }

        private bool _isVpnAlertOpen;
        public bool _IsVpnAlertOpen
        {
            get => _isVpnAlertOpen;
            set
            {
                if (_isVpnAlertOpen != value)
                {
                    _isVpnAlertOpen = value;
                    OnPropertyChanged(nameof(_IsVpnAlertOpen));
                }
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public UserSettings UserSettings { get; set; }

        public Settings()
        {
            InitializeComponent();
            Database.Initialize();
            this.DataContext = this;
            UserSettings = new UserSettings();

            // Subscribe to events
            PrivateModeAlertControl.OkClicked += PrivateModeAlertControl_OkClicked;
            PrivateModeAlertControl.CloseClicked += PrivateModeAlertControl_CloseClicked;

            VpnAlertControl.OkClicked += VpnAlertControl_OkClicked;
            VpnAlertControl.CloseClicked += VpnAlertControl_CloseClicked;

            this.Loaded += Page_Load;

            // Close popup when window loses focus
            Application.Current.Deactivated += (s, e) =>
            {
                if (TimePopup.IsOpen)
                    TimePopup.IsOpen = false;

                //if (ActivityTrackingScopePopup.IsOpen)
                //    ActivityTrackingScopePopup.IsOpen = false;
            };
            // Detect click outside popup
            this.PreviewMouseDown += (s, e) =>
            {
                if (TimePopup.IsOpen)
                {
                    // If click was outside popup and not on TimeBorder
                    if (!TimePopup.IsMouseOver && !TimePopup.IsMouseOver)
                        TimePopup.IsOpen = false;
                }
                //if (ActivityTrackingScopePopup.IsOpen)
                //{
                //    if (!ActivityTrackingScopePopup.IsMouseOver && !ActivityTrackingScopePopup.IsMouseOver)
                //        ActivityTrackingScopePopup.IsOpen = false;
                //}
            };


            // Populate hours dynamically
            for (int i = 0; i <= 6; i++)
            {
                HourList.Items.Add(new ListBoxItem { Content = i.ToString("D2") }); // D2 → 01, 02 ... 24
            }

            for (int i = 0; i < 60; i++)
            {
                MinuteList.Items.Add(new ListBoxItem { Content = i.ToString("D2") });
            }
            // Get reference to MainWindow
            MainWindow mainWindow = Application.Current.MainWindow as MainWindow;
            
        }




        private async void Page_Load(object sender, RoutedEventArgs e)
        {
            // Load settings from DB
            var settings = await Database.GetUserSettings();
            if (settings.Count > 0)
            {
                ActiveWindowTracker.TrackPrivateModeEnabled = settings[0].TrackPrivateMode;
            }

            var transform = new TranslateTransform();
            this.RenderTransform = transform;
            double pageWidth = this.ActualWidth;
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = pageWidth,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                AccelerationRatio = 0.2,
                DecelerationRatio = 0.8
            };
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
            await LoadSettingAsync();


        }

        // Make this async

        public async Task LoadSettingAsync()
        {
            var settingsList = await Database.GetUserSettings();
            if (settingsList.Count > 0)
            {
                UserSettings = settingsList[0];

                TrackPrivateModeToggle.IsChecked = UserSettings.TrackPrivateMode;
                TrackVPNToggle.IsChecked = UserSettings.TrackVPN;

                ActiveWindowTracker.TrackPrivateModeEnabled = UserSettings.TrackPrivateMode;
                ActiveWindowTracker.TrackVPNEnabled = UserSettings.TrackVPN;

                // Load BreakTime
                if (!string.IsNullOrEmpty(UserSettings.BreakTime))
                {
                    var parts = UserSettings.BreakTime.Split(':');
                    if (parts.Length >= 2)
                    {
                        HourText.Text = parts[0];
                        MinuteText.Text = parts[1];
                    }
                }
                else
                {
                    HourText.Text = "00";
                    MinuteText.Text = "00";
                }

                // Set value from UserSettings
                NotifyEveryTime.IsChecked = UserSettings.NotifyBreakEveryTime;
             
            }
        }


        private void PrivateModeAlertControl_OkClicked(object sender, EventArgs e)
        {
            PrivateModeAlertPopup.IsOpen = false;
        }
        private void PrivateModeAlertControl_CloseClicked(object sender, EventArgs e)
        {
            PrivateModeAlertPopup.IsOpen = false;
        }


        private void VpnAlertControl_OkClicked(object sender, EventArgs e)
        {
            VpnAlertPopup.IsOpen = false;
        }
        private void VpnAlertControl_CloseClicked(object sender, EventArgs e)
        {
            VpnAlertPopup.IsOpen = false;
        }

        private async void TrackPrivateModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (TrackPrivateModeToggle.IsChecked.HasValue)
            {
                bool isOn = TrackPrivateModeToggle.IsChecked.Value;
                // Check previous value BEFORE updating it
                bool wasOn = UserSettings.TrackPrivateMode;

                await Database.UpdateTrackPrivateModeAsync(isOn);
                UserSettings.TrackPrivateMode = isOn;

                // Update the static flag
                ActiveWindowTracker.TrackPrivateModeEnabled = isOn;

                // Show popup only if it changed from true -> false
                if (!isOn && wasOn)
                {
                    PrivateModeAlertPopup.IsOpen = true;
                }
            }
        }
        private async void TrackVPNToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (TrackVPNToggle.IsChecked.HasValue)
            {
                bool isOn = TrackVPNToggle.IsChecked.Value;
                bool wasOn = UserSettings.TrackVPN;

                await Database.UpdateTrackVPNAsync(isOn);
                UserSettings.TrackVPN = isOn;

                // Update the static flag
                ActiveWindowTracker.TrackVPNEnabled = isOn;

                // Show popup only if it change from true -> false
                if (!isOn && wasOn)
                {
                    VpnAlertPopup.IsOpen = true;
                }
            }
        }

        private async void NotifyEveryTime_Changed(object sender, RoutedEventArgs e)
        {
            await Database.EnsureNotifyBreakEveryTimeColumn();
            if (NotifyEveryTime.IsChecked.HasValue)
            {
                bool isOn = NotifyEveryTime.IsChecked.Value;
                await Database.UpdateNotifyEveryTime(isOn);
                UserSettings.NotifyBreakEveryTime = isOn; // <-- update in-memory
            }
        }





        // Focause Mode Grid Click
        public void FocauseModeGrid_Click(object sender, RoutedEventArgs e)
        {
            if (DetailsGrid.Visibility == Visibility.Collapsed)
            {
                DetailsGrid.Visibility = Visibility.Visible;
                ChevronIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ChevronUp;
            }
            else
            {
                DetailsGrid.Visibility = Visibility.Collapsed;
                ChevronIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ChevronDown;
            }
        }

        private void TimeBorder_Click(object sender, MouseButtonEventArgs e)
        {
            TimePopup.IsOpen = !TimePopup.IsOpen;
        }

        private void Hour_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && lb.SelectedItem is ListBoxItem item)
                HourText.Text = item.Content.ToString();
        }

        private void Minute_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && lb.SelectedItem is ListBoxItem item)
                MinuteText.Text = item.Content.ToString();
        }

        private void HourUpIcon_Click(object sender, MouseButtonEventArgs e)
        {
            ScrollListBox(HourList, -1);
        }
        private void HourDownIcon_Click(object sender, MouseButtonEventArgs e)
        {
            ScrollListBox(HourList, 1);
        }
        private void MiniutUpIcon_Click(object sender, MouseButtonEventArgs e)
        {
            ScrollListBox(MinuteList, -1);
        }
        private void MiniutDownIcon_Click(object sender, MouseButtonEventArgs e)
        {
            ScrollListBox(MinuteList, 1);
        }
        private void ScrollListBox(ListBox listBox, int direction)
        {
            if (listBox.Items.Count == 0) return;

            int newIndex = listBox.SelectedIndex + direction;

            if (newIndex < 0) newIndex = 0;
            if (newIndex >= listBox.Items.Count) newIndex = listBox.Items.Count - 1;

            listBox.SelectedIndex = newIndex;
            listBox.ScrollIntoView(listBox.SelectedItem);
        }


        private async void TimePopup_Closed(object sender, EventArgs e)
        {
            var hour = HourText.Text;
            var minute = MinuteText.Text;

            var timeString = $"{hour}:{minute}";
            _targetTime = timeString;

            await Database.EnsureBreakTimeColumn();
            await Database.SaveBreakTimeToDatabase(_targetTime);

          
        }


        // For Active App Scop Setting
        //private void ActivityTrackingScope_Click(object sender, MouseButtonEventArgs e)
        //{
        //    ActivityTrackingScopePopup.IsOpen = !ActivityTrackingScopePopup.IsOpen;
        //}

        //private async void ActivityTrackingScopePopup_Closed(object sender, EventArgs e)
        //{
        //    var selectedScope = ActivityTrackingScopeListBox.SelectedItem;
        //}

        private void BackIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!NavigationService.CanGoBack)
            {
                return;
            }

            var transform = new TranslateTransform();
            this.RenderTransform = transform;

            double pageWidth = this.ActualWidth;
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = pageWidth,
                Duration = TimeSpan.FromMilliseconds(300),
                AccelerationRatio = 0.2,
                DecelerationRatio = 0.8
            };
            animation.Completed += (s, a) => NavigationService.GoBack();

            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

      

        



    }
}