using FocusTrack.Controls;
using FocusTrack.model;
using FocusTrack.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
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

namespace FocusTrack.Pages
{

    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : Page
    {
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

            this.Loaded += Page_Load;
        }

      


        private async void Page_Load(object sender, RoutedEventArgs e)
        {
            // Load settings from DB
            var settings = await Database.GetUserSettings();
            if (settings.Count > 0)
            {
                ActiveWindowTracker.TrackPrivateModeEnabled = settings[0].TrackPrivateMode;
                System.Diagnostics.Debug.WriteLine($"TrackPrivateMode loaded on Settings page: {settings[0].TrackPrivateMode}");
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

        private async Task LoadSettingAsync()
        {
            var settingsList = await Database.GetUserSettings();
            System.Diagnostics.Debug.WriteLine($"Settings count loaded: {settingsList.Count}");

            if (settingsList.Count > 0)
            {
                UserSettings = settingsList[0];

                TrackPrivateModeToggle.IsChecked = UserSettings.TrackPrivateMode;
                TrackVPNToggle.IsChecked = UserSettings.TrackVPN;

                ActiveWindowTracker.TrackPrivateModeEnabled = UserSettings.TrackPrivateMode;
                ActiveWindowTracker.TrackVPNEnabled = UserSettings.TrackVPN;

              
            }
        }

        private void PrivateModeAlertControl_OkClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("PrivateModeAlert OK clicked.");
            PrivateModeAlertPopup.IsOpen = false;
        }
        private void PrivateModeAlertControl_CloseClicked(object sender, EventArgs e)
        {
            PrivateModeAlertPopup.IsOpen = false;
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

                System.Diagnostics.Debug.WriteLine($"TrackPrivateMode updated: {isOn}");

                // Show popup only if it changed from true → false
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

                await Database.UpdateTrackVPNAsync(isOn);
                UserSettings.TrackVPN = isOn; // <-- fix here

                // Update the static flag
                ActiveWindowTracker.TrackVPNEnabled = isOn;

                System.Diagnostics.Debug.WriteLine($"TrackVPN updated: {isOn}");
            }
        }




        private void BackIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!NavigationService.CanGoBack) { 
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
            animation.Completed += (s,a) =>NavigationService.GoBack();

            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void ShowPrivateModeAlertPopup()
        {
            // Calculate center position
            double horizontalCenter = (this.ActualWidth - PrivateModeAlertPopup.ActualWidth) / 2;
            double verticalCenter = (this.ActualHeight - PrivateModeAlertPopup.ActualHeight) / 2;

            PrivateModeAlertPopup.HorizontalOffset = horizontalCenter;
            PrivateModeAlertPopup.VerticalOffset = verticalCenter;

            PrivateModeAlertPopup.IsOpen = true;
        }




    }
}
