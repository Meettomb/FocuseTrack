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

            // 🔹 Ensure DB file & AppUsage table exist before anything else uses it
            Database.Initialize();

            lastStart = DateTime.Now;

            timer = new System.Timers.Timer(5000);
            timer.AutoReset = true;
            timer.Start();

            this.DataContext = this;

            StartupHelper.AddToStartup();

            
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


      

        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            //MessageBox.Show("Settings button clicked!");
        }
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to home page
            MainFrame.Navigate(new HomePage());
        }

        private void AppOpenTimeButton_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new AppOpenCountPage());
        }




    }
}
