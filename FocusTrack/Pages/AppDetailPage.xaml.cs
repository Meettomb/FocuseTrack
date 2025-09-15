using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace FocusTrack.Pages
{
    public partial class AppDetailPage : Page
    {
        public ObservableCollection<AppUsageDetail> AppUsageDetails { get; set; } = new ObservableCollection<AppUsageDetail>();

        private string appName;
        private DateTime date;

        public AppDetailPage()
        {
            InitializeComponent();
            this.DataContext = this;  // Important for binding
            Loaded += AppDetailPage_Loaded;
        }

        private async void AppDetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            var transform = new TranslateTransform();
            this.RenderTransform = transform;

            // Slide in from right to normal position
            double pageWidth = this.ActualWidth;

            var slideIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = pageWidth,  // Start completely off-screen right
                To = 0,            // Slide to normal position
                Duration = TimeSpan.FromMilliseconds(120),
                DecelerationRatio = 0.9
            };

            transform.BeginAnimation(TranslateTransform.XProperty, slideIn);

            if (this.NavigationService?.Source == null)
                return;

            var query = NavigationService.Source.OriginalString;
            var queryIndex = query.IndexOf('?');

            if (queryIndex < 0)
                return;

            var queryParams = ParseQueryString(query.Substring(queryIndex));

            if (queryParams.TryGetValue("appName", out string app) &&
                queryParams.TryGetValue("date", out string dateStr) &&
                DateTime.TryParse(dateStr, out DateTime parsedDate))
            {
                appName = app;
                date = parsedDate;

                await Task.Delay(100);  // Small delay to ensure UI is ready

                await LoadAppDetailResultsByAppName(appName, date);
            }
        }

        private Dictionary<string, string> ParseQueryString(string query)
        {
            return query.TrimStart('?')
                        .Split('&')
                        .Where(part => !string.IsNullOrWhiteSpace(part))
                        .Select(part => part.Split('='))
                        .ToDictionary(
                            kv => Uri.UnescapeDataString(kv[0]),
                            kv => Uri.UnescapeDataString(kv[1])
                        );
        }

        private async Task LoadAppDetailResultsByAppName(string appName, DateTime date, DateTime? start = null, DateTime? end = null)
        {
            var groupedAppUsages = await Database.GetAppDetailResultsByAppName(appName, date, start, end);

            AppUsageDetails.Clear();

            if (!groupedAppUsages.Any())
            {
                MessageBox.Show("No data available.", "Info");
                return;
            }

            foreach (var item in groupedAppUsages)
            {
                AppUsageDetails.Add(new AppUsageDetail
                {
                    WindowTitle = item.WindowTitle,
                    StartTime = item.StartTime.ToString("HH:mm:ss"),
                    EndTime = item.EndTime.ToString("HH:mm:ss"),
                    Duration = item.Duration.ToString()
                });
            }
        }
        private void BackIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!this.NavigationService.CanGoBack)
                return;

            // Create a TranslateTransform for the page
            var transform = new TranslateTransform();
            this.RenderTransform = transform;

            double pageWidth = this.ActualWidth;

            var slideOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,            // Current position
                To = pageWidth,      // Slide off-screen right
                Duration = TimeSpan.FromMilliseconds(200),
                AccelerationRatio = 0.1, // Slightly start slow
                DecelerationRatio = 0.9  // Slow down at the end
            };

            slideOut.Completed += (s, _) => this.NavigationService.GoBack();

            transform.BeginAnimation(TranslateTransform.XProperty, slideOut);

        }


    }
}


    public class AppUsageDetail
    {
        public string WindowTitle { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public string Duration { get; set; }
    }

  
