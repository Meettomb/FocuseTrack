using System;
using System.Media;
using System.Windows;
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

            // Close automatically after 5 seconds
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(5);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                this.Close();
            };
            timer.Start();
        }
    }
}
