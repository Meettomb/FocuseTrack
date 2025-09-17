using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FocusTrack.Controls
{
    public partial class VpnAlert : UserControl
    {
        public List<string> LeftColumnVpnList { get; set; }
        public List<string> RightColumnVpnList { get; set; }

        public event EventHandler OkClicked;
        public event EventHandler CloseClicked;

        public VpnAlert()
        {
            InitializeComponent();

            // Full dynamic VPN list
            List<string> allVpnList = new List<string>
            {
                "NordVPN",
                "ExpressVPN",
                "Surfshark",
                "ProtonVPN",
                "ProtonVPN Service",
                "OpenVPN",
                "Ivacy",
                "IVPN",
                "Windscribe",
                "Hide.me",
                "PrivateVPN",
                "HideIPVPN",
                "StrongVPN",
                "Cryptostorm",
                "PIA (Private Internet Access)",
                "PIA Service",
                "CactusVPN",
                "Perfect Privacy",
                "TorGuard",
                "VPN.ac",
                "AirVPN",
                "Mullvad",
                "AirVPN Service",
                "TunnelBear",
                "Tunnelblick",
                "SoftEtherVPN",
                "SoftEther",
                "OpenConnect",
                "FortiClient",
                "Fortinet",
                "AnyConnect",
                "Cisco AnyConnect",
                "FortiClient SSL VPN",
                "FortiVPN",
                "Fortinet VPN",
                "FortiVPN Client"
            };
            
            int vpnCount = allVpnList.Count;

            Console.WriteLine($"Total VPN entries: {vpnCount}");

            // Split the list in half for two columns
            int middleIndex = (allVpnList.Count + 1) / 2;
            LeftColumnVpnList = allVpnList.Take(middleIndex).ToList();
            RightColumnVpnList = allVpnList.Skip(middleIndex).ToList();

            // Set DataContext
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            OkClicked?.Invoke(this, EventArgs.Empty);
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
