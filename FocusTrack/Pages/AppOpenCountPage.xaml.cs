using FocusTrack.Controls;
using FocusTrack.helpers;
using FocusTrack.Model;
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

namespace FocusTrack.Pages
{
    /// <summary>
    /// Interaction logic for AppOpenCountPage.xaml
    /// </summary>
    public partial class AppOpenCountPage : Page, INotifyPropertyChanged
    {
        public ObservableCollection<AppOpenCount> AppUsages { get; set; }

        public ImageSource IconImage { get; set; }
        private System.Timers.Timer timer;
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

        private bool _isCalendarOpen;

        public bool IsCalendarOpen
        {
            get => _isCalendarOpen;
            set
            {
                if (_isCalendarOpen != value)
                {
                    _isCalendarOpen = value;
                    OnPropertyChanged(nameof(IsCalendarOpen));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public AppOpenCountPage()
        {
            InitializeComponent();
            if (CalendarPopup.Child is Border border && border.Child is CustomCalendar calendar)
            {
                calendar.DateSelected += (s, date) =>
                {
                    CalendarPopup.IsOpen = false; // close the popup
                };
            }

            AppUsages = new ObservableCollection<AppOpenCount>();

            //  Ensure DB file & AppUsage table exist before anything else uses it
            Database.Initialize();

            lastStart = DateTime.Now;

            timer = new System.Timers.Timer(1000);
            //timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();

            this.DataContext = this;

            this.Loaded += async (_, __) =>
            {
                //await LoadDefaultGraph();    // load chart
                await LoadDefaultAppCount(); // load grid data safely

                SelectedDate = DateTime.Today;

            };

        }
        private string lastAppName = "";
        private string lastWindowTitle = "";
        private DateTime lastStartTime;

        private (string AppName, string Title, string ExePath, byte[] AppIcon) lastActive = (null, null, null, null);

        //private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        //{
        //    var active = ActiveWindowTracker.GetActiveWindowInfo();

        //    // Check if tuple is null (tuple itself can't be null, but fields can)
        //    if (string.IsNullOrWhiteSpace(active.AppName))
        //        return;

        //    // Check if app or window changed
        //    if (active.AppName != lastActive.AppName || active.Title != lastActive.Title)
        //    {
        //        // Save previous session if any
        //        if (!string.IsNullOrEmpty(lastActive.AppName))
        //        {
        //            _ = Task.Run(async () =>
        //            {
        //                try
        //                {
        //                    await Database.SaveSessionAsync(
        //                        lastActive.AppName,
        //                        lastActive.Title,
        //                        lastStartTime,
        //                        DateTime.Now,
        //                        lastActive.ExePath
        //                    );
        //                }
        //                catch (Exception ex)
        //                {
        //                    Debug.WriteLine(ex.Message);
        //                }
        //            });
        //        }

        //        // Start new session
        //        lastActive = active;
        //        lastStartTime = DateTime.Now;

        //        // Refresh UI
        //        _ = Task.Run(async () =>
        //        {
        //            try
        //            {
        //                await RefreshUIAsync();
        //            }
        //            catch (Exception ex)
        //            {
        //                Debug.WriteLine(ex.Message);
        //            }
        //        });
        //    }
        //}



        private async Task RefreshUIAsync()
        {
            var start = SelectedDate.Date;
            DateTime end = SelectedDate.Date.AddDays(1).AddSeconds(-1);
            var allData = await Database.GetAppOpenCountAsync(start, end);

            Dispatcher.Invoke(() =>
            {
                RangeSelecter.SelectedIndex = 0;
                AppUsages.Clear();
                foreach (var item in allData)
                    AppUsages.Add(item);
            });
        }


        private async void RangeSelectot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RangeSelecter.SelectedItem is ComboBoxItem selected)
            {
                string range = selected.Tag.ToString();

                DateTime today = DateTime.Today;
                DateTime now = DateTime.Now;
                DateTime rangeStart, rangeEnd;
                switch (range) {
                    case "today":
                        rangeStart = today;
                        rangeEnd = now;
                        break;
                    case "7d":
                        rangeStart = today.AddDays(-6); // last 7 days including today
                        rangeEnd = now;
                        break;

                    case "1m":
                        rangeStart = today.AddMonths(-1).AddDays(1); // last 1 month including today
                        rangeEnd = now;
                        break;

                    case "3m":
                        rangeStart = today.AddMonths(-3).AddDays(1); // last 3 months including today
                        rangeEnd = now;
                        break;

                    case "overall":
                        rangeStart = DateTime.MinValue;
                        rangeEnd = now;
                        break;

                    default:
                        rangeStart = today;
                        rangeEnd = now;
                        break;
                }

                await LoadAppCount(rangeStart, rangeEnd);
            }
        }


        private async Task LoadAppCount(DateTime? start, DateTime? end)
        {
            var usages = await Database.GetAppOpenCountAsync(start, end);

            Dispatcher.Invoke(() =>
            {
                if (AppUsages == null)
                    AppUsages = new ObservableCollection<AppOpenCount>();

                AppUsages.Clear();

                foreach (var usage in usages)
                {
                    AppUsages.Add(usage);
                }

                if (AppUsageGrid != null && AppUsageGrid.ItemsSource == null)
                    AppUsageGrid.ItemsSource = AppUsages;
            });
            UpdateTotalUsage();
        }
        private async Task LoadDefaultAppCount()
        {
           await LoadAppCount(DateTime.Today, DateTime.Now);

        }
        public void UpdateTotalUsage()
        {
            int totalOpens = AppUsages.Sum(x => x.OpenCount);

            TotalAppOpenCountTextBlock.Text = $"{totalOpens}";
        }



        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Grid grid && grid.Tag is AppUsage clickedInfo)
            {
                // Check selected range
                if (RangeSelecter.SelectedItem is ComboBoxItem selectedRange)
                {
                    string range = selectedRange.Tag.ToString();

                    if (range != "today")
                    {
                        MessageBox.Show("You can only view details for a single day.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                        return; // Stop navigation
                    }
                }

                // Proceed with navigation for "today" only
                var appName = clickedInfo.AppName;
                var date = SelectedDate;

                var uri = new Uri($"/Pages/AppDetailPage.xaml?appName={Uri.EscapeDataString(appName)}&date={date:yyyy-MM-dd}", UriKind.Relative);
                this.NavigationService.Navigate(uri);
            }
        }



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
        private void Calendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CalendarPopup != null)
                CalendarPopup.IsOpen = false; // close popup when a date is picked
        }

        private async Task LoadDataForSelectedDate()
        {
            DateTime start = SelectedDate.Date;
            DateTime end = SelectedDate.Date.AddDays(1).AddSeconds(-1);

            //var hourlyData = await Database.GetHourlyUsageAsync(start, end);
            //LoadGraphData(hourlyData);

            await LoadAppCount(start, end);

        }




    }
}
