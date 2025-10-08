using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FocusTrack.Controls
{
    public partial class CustomCalendar : UserControl, INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<DateTime> DateSelected;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private DateTime _currentMonth;
        private DateTime currentMonth
        {
            get => _currentMonth;
            set
            {
                if (_currentMonth != value)
                {
                    _currentMonth = value;
                    OnPropertyChanged(nameof(DisplayMonth)); // notify UI
                    RenderCalendar(); // also refresh calendar when month changes
                }
            }
        }

        public string DisplayMonth => currentMonth.ToString("MMMM yyyy");

        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(
                nameof(SelectedDate),
                typeof(DateTime),
                typeof(CustomCalendar),
                new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault)
            );

        public DateTime SelectedDate
        {
            get => (DateTime)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        public CustomCalendar()
        {
            InitializeComponent();
            currentMonth = DateTime.Today; // this now sets the property, not a duplicate field
        }

        private void RenderCalendar()
        {
            DaysGrid.Children.Clear();

            DateTime firstDay = new DateTime(currentMonth.Year, currentMonth.Month, 1);
            int skipDays = (int)firstDay.DayOfWeek;

            // Add empty placeholders for alignment
            for (int i = 0; i < skipDays; i++)
            {
                DaysGrid.Children.Add(new TextBlock());
            }

            int daysInMonth = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);

            for (int day = 1; day <= daysInMonth; day++)
            {
                DateTime currentDay = new DateTime(currentMonth.Year, currentMonth.Month, day);

                // Skip days in the future
                if (currentDay > DateTime.Today)
                    break;

                Button btn = new Button
                {
                    Content = day.ToString(),
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    Margin = new Thickness(2),
                    Padding = new Thickness(10),
                    FontWeight = FontWeights.Bold,
                    Tag = currentDay
                };

                if (currentDay == DateTime.Today)
                    btn.Background = Brushes.LightGreen; // highlight today

                btn.Click += DayButton_Click;
                DaysGrid.Children.Add(btn);
            }
        }


        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DateTime date)
            {
                SelectedDate = date;
                DateSelected?.Invoke(this, date); // raise event
            }
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            currentMonth = currentMonth.AddMonths(-1);
            RenderCalendar();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            currentMonth = currentMonth.AddMonths(1);
            RenderCalendar();
        }

       
    }
}
