using System;
using System.Media;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FocusTrack
{
    public partial class BreakNotificationWindow : Window
    {
        public BreakNotificationWindow(string soundPath = null)
        {
            InitializeComponent();

            // Play sound if exists
            if (!string.IsNullOrEmpty(soundPath) && System.IO.File.Exists(soundPath))
            {
                var player = new SoundPlayer(soundPath);
                player.Play();
            }
            else
            {
                SystemSounds.Beep.Play();
            }

            // Close automatically after 59 seconds
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(59);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                this.Close();
            };
            timer.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Start pulse animation for clock
            Storyboard sb = (Storyboard)FindResource("PulseAnimation");
            sb.Begin();
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Stop pulse animation
            if (FindResource("PulseAnimation") is Storyboard sb)
                sb.Stop();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
