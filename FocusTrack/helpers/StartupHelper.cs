using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace FocusTrack.helpers
{
    internal class StartupHelper
    {
        public static void AddToStartup()
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                RegistryKey rk = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

                if (rk.GetValue("FocusTrack") == null)
                {
                    rk.SetValue("FocusTrack", exePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add to startup: " + ex.Message);
            }
        }
    }
}
