using FocusTrack.Helpers;
using FocusTrack.model;
using FocusTrack.Pages;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;                // for File.Exists
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
namespace FocusTrack
{
    public static class ActiveWindowTracker
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]

        private static extern bool CloseHandle(IntPtr hObject);


        // Skip minimized windows
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

       
        
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;


        #region Virtual Desktop Manager API
        [ComImport]
        [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IVirtualDesktopManager
        {
            bool IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
            Guid GetWindowDesktopId(IntPtr topLevelWindow);
            void MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
        }

        [ComImport]
        [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
        class VirtualDesktopManagerClass
        {
        }
        #endregion



        public static bool TrackPrivateModeEnabled = true;
        public static bool TrackVPNEnabled = true;



        // Ignore system/host processes
        private static readonly HashSet<string> IgnoredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LockApp",
            "SearchHost",
            "RuntimeBroker",
            "System",
            "Idle",
            "smss",
            "csrss",
            "wininit",
            "services",
            "lsass",
            "syastem", 
            
            
            // Shell / host processes
            "shellexperiencehost.exe",
            "applicationframehost.exe",
            "runtimebroker.exe",
            "searchhost.exe",
            "textinputhost.exe",
            "lockapp.exe",
        };

        // Map for known UWP apps running under ApplicationFrameHost
        private static readonly Dictionary<string, (string FriendlyName, string IconPath)> UwpApps =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "WhatsApp", ("WhatsApp", "Assets/Icons/WhatsApp.png") },
                { "Spotify", ("Spotify", "Assets/Icons/spotify.png") },
                { "Microsoft Teams", ("Microsoft Teams", "Assets/Icons/teams.png") },
                { "Telegram", ("Telegram", "Assets/Icons/telegram.png") },
                { "Netflix", ("Netflix", "Assets/Icons/netflix.png") },
                { "Mail", ("Mail", "Assets/Icons/mail.png") },
                { "Calendar", ("Calendar", "Assets/Icons/calendar.png") },
                { "Photos", ("Photos", "Assets/Icons/photos.png") },
                { "Xbox", ("Xbox", "Assets/Icons/xbox.png") },
                { "Groove Music", ("Groove Music", "Assets/Icons/groove.png") },
                { "Movies & TV", ("Movies & TV", "Assets/Icons/movies.png") },
                { "Sticky Notes", ("Sticky Notes", "Assets/Icons/stickynotes.png") },
                { "OneNote", ("OneNote", "Assets/Icons/onenote.png") },
                { "Skype", ("Skype", "Assets/Icons/skype.png") },
                { "Microsoft Store", ("Microsoft Store", "Assets/Icons/store.png") },
                { "Weather", ("Weather", "Assets/Icons/weather.png") },
                { "Maps", ("Maps", "Assets/Icons/maps.png") },
                { "Alarms & Clock", ("Alarms & Clock", "Assets/Icons/clock.png") },
                { "Clock", ("Alarms & Clock", "Assets/Icons/clock.png") },
                { "Alarm", ("Alarms & Clock", "Assets/Icons/clock.png") }
            };

        public static (string AppName, string Title, string ExePath, byte[] AppIcon) GetActiveWindowInfo()
        {
            // Get installed VPNs
            var installedVPNs = GetInstalledVPNs();



            // If user disabled tracking private mode, skip ALL private browsers
            Process currentProcess = GetForegroundProcess();
            if (!TrackPrivateModeEnabled && IsPrivateBrowser(currentProcess))
            {
                return ("", "", "", null);
            }


            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ("", "", "", null);

            // Skip windows not on the current virtual desktop
            if (!IsWindowOnCurrentDesktop(hwnd))
            {
                return ("", "", "", null);
            }

            if (IsIconic(hwnd))
            {
                Debug.WriteLine("[GetActiveWindowInfo] Window is minimized — skipping tracking.");
                return ("", "", "", null);
            }
            else {
                //Debug.WriteLine("[GetActiveWindowInfo] Window is active and visible.");
            }
            GetWindowThreadProcessId(hwnd, out uint pid);

            Process proc = null;
            try
            {
                proc = Process.GetProcessById((int)pid);
            }
            catch
            {
                //Debug.WriteLine($"[GetActiveWindowInfo] Failed to get process for PID {pid}");
                return ("Unknown", "", "", null);
            }


            //  Skip minimized windows
            if (IsIconic(hwnd))
            {
                //Debug.WriteLine("[GetActiveWindowInfo] Window is minimized — skipping tracking.");
                return ("", "", "", null);
            }
                // Skip desktop when app minimized
            if (proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(proc.MainWindowTitle) ||
                 proc.MainWindowTitle.Equals("Program Manager", StringComparison.OrdinalIgnoreCase)))
            {
                //Debug.WriteLine("[GetActiveWindowInfo] Desktop or minimized app detected — skipping tracking.");
                return ("", "", "", null);
            }

            // Ignore unwanted system processes
            if (IgnoredProcesses.Contains(proc.ProcessName))
                return ("", "", "", null);


            string exePath = GetProcessPath(proc);
            //Debug.WriteLine($"[GetActiveWindowInfo] Process: {proc.ProcessName}, Path: {exePath}");


            // Block installed VPN apps if TrackVPNEnabled is false
            if (!TrackVPNEnabled)
            {
                foreach (var vpn in installedVPNs)
                {
                    if (proc.ProcessName.ToLower().Contains(vpn.ToLower()))
                    {
                        // Skip tracking this VPN app
                        return ("", "", "", null);
                    }
                }
            }

            //bool isOnCurrent = IsWindowOnCurrentDesktop(hwnd);
            //Debug.WriteLine($"[Desktop Check] {proc.ProcessName} on current desktop? {isOnCurrent}");



            // Get window title
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            string windowTitle = sb.ToString();
            //Debug.WriteLine($"[GetActiveWindowInfo] Window Title: {windowTitle}");

            // Ignore Windows "System Tray overflow window" or empty Explorer windows
            if (proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                (proc.MainWindowTitle.Equals("Program Manager", StringComparison.OrdinalIgnoreCase) ||
                proc.MainWindowTitle.Equals("System tray overflow window", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(proc.MainWindowTitle)))
            {
                //return ("", "", "", null);
                return ("Desktop", "No active window", "C:\\Windows\\explorer.exe", null);
            }

            if (string.IsNullOrWhiteSpace(proc.MainWindowTitle))
            {
                return ("", "", "", null);
            }



            // Extract app icon
            byte[] appIcon = IconHelper.GetIconBytes(exePath);
            if (appIcon != null && appIcon.Length > 0) { 
                //Debug.WriteLine($"[GetActiveWindowInfo] Extracted icon for {exePath}, size = {appIcon.Length} bytes");
            }
            else
                //Debug.WriteLine($"[GetActiveWindowInfo] Failed to extract icon for {exePath}");

            // Handle UWP apps running under ApplicationFrameHost
            if (proc.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(windowTitle))
                {
                    foreach (var kvp in UwpApps)
                    {
                        if (windowTitle.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string realExe = TryGetRealUwpExe(windowTitle);
                            if (!string.IsNullOrEmpty(realExe) && File.Exists(realExe))
                            {
                                exePath = realExe;
                                appIcon = IconHelper.GetIconBytes(exePath);
                            }

                            // Always fallback if icon not found or still ApplicationFrameHost
                            if (appIcon == null || appIcon.Length == 0 || exePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                appIcon = GetFallbackUwpIcon(windowTitle);
                                if (appIcon != null) { 
                                    //Debug.WriteLine($"Used fallback icon for {kvp.Value.FriendlyName}, size={appIcon.Length} bytes");
                                }
                            }

                            return (kvp.Value.FriendlyName, windowTitle, exePath, appIcon);
                        }
                    }
                }
            }


            string appName = GetFriendlyAppName(proc, proc.ProcessName);
            //Debug.WriteLine($"[GetActiveWindowInfo] Final app name: {appName}");

            return (appName, windowTitle, exePath, appIcon);
        }

        private static bool IsWindowOnCurrentDesktop(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero)
                    return false;

                var vdm = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
                return vdm.IsWindowOnCurrentVirtualDesktop(hwnd);
            }
            catch
            {
                // If API not available or COM fails, assume true (so it doesn't break)
                return true;
            }
        }


        private static string GetProcessPath(Process proc)
        {
            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)proc.Id);
                if (hProcess == IntPtr.Zero) return "";

                try
                {
                    int capacity = 1024;
                    StringBuilder builder = new StringBuilder(capacity);
                    if (QueryFullProcessImageName(hProcess, 0, builder, ref capacity))
                        return builder.ToString();
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch { }
            return "";
        }
        private static string TryGetRealUwpExe(string windowTitle)
        {
            try
            {
                // Look at all processes with a visible window
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(proc.MainWindowTitle)) continue;

                        // Match by window title
                        if (proc.MainWindowTitle.IndexOf(windowTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string exe = proc.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(exe) && exe.Contains("WindowsApps"))
                            {
                                return exe; // This should be WhatsApp.exe inside WindowsApps
                            }
                        }
                    }
                    catch
                    {
                        // Access denied for some system processes, ignore
                    }
                }
            }
            catch { }

            return null;
        }


        private static int GetParentProcessId(Process process)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId=" + process.Id))
                {
                    foreach (var obj in searcher.Get())
                    {
                        return Convert.ToInt32(obj["ParentProcessId"]);
                    }
                }
            }
            catch { }
            return -1;
        }


        private static string GetFriendlyAppName(Process proc, string processName)
        {
            // Dictionary for normal apps
            Dictionary<string, string> appNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "chrome", "Google Chrome" },
                { "msedge", "Microsoft Edge" },
                { "firefox", "Mozilla Firefox" },
                { "opera", "Opera Browser" },
                { "iexplore", "Internet Explorer" },
                { "code", "Visual Studio Code" },
                { "devenv", "Visual Studio" },
                { "sublime_text", "Sublime Text" },
                { "pycharm", "PyCharm" },
                { "webstorm", "WebStorm" },
                { "androidstudio", "Android Studio" },
                { "eclipse", "Eclipse IDE" },
                { "intellij", "IntelliJ IDEA" },
                { "notepad", "Notepad" },
                { "notepad++", "Notepad++" },
                { "winword", "Microsoft Word" },
                { "excel", "Microsoft Excel" },
                { "powerpnt", "Microsoft PowerPoint" },
                { "outlook", "Microsoft Outlook" },
                { "onenote", "Microsoft OneNote" },
                { "access", "Microsoft Access" },
                { "teams", "Microsoft Teams" },
                { "zoom", "Zoom" },
                { "slack", "Slack" },
                { "discord", "Discord" },
                { "skype", "Skype" },
                { "telegram", "Telegram" },
                { "whatsapp", "WhatsApp" },
                { "spotify", "Spotify" },
                { "vlc", "VLC Media Player" },
                { "wmplayer", "Windows Media Player" },
                { "itunes", "iTunes" },
                { "foobar2000", "Foobar2000" },
                { "studio64", "Android Studio" }
            };

            if (appNameMap.TryGetValue(processName, out string friendlyName))
                return friendlyName;

            try
            {
                string productName = proc?.MainModule?.FileVersionInfo?.ProductName;
                if (!string.IsNullOrWhiteSpace(productName))
                    return productName;
            }
            catch { }

            return processName;
        }

        private static byte[] GetFallbackUwpIcon(string windowTitle)
        {
            foreach (var kvp in UwpApps)
            {
                if (windowTitle.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string iconPath = kvp.Value.IconPath;
                    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconPath);

                    if (File.Exists(fullPath))
                        return File.ReadAllBytes(fullPath); // only return the byte array
                }
            }
            return null;
        }



        // Check if current process is a private browser window
        private static bool IsPrivateBrowser(Process proc)
        {
            try
            {
                if (proc == null) return false;
                string procName = proc.ProcessName.ToLowerInvariant();
                string cmdLine = GetCommandLine(proc) ?? "";
                string title = proc.MainWindowTitle?.ToLower() ?? "";

                // Chromium family
                if ((procName.Contains("chrome") || procName.Contains("opera") ||
                     procName.Contains("brave") || procName.Contains("vivaldi")) &&
                    cmdLine.Contains("--incognito"))
                    return true;

                // Edge
                if (procName.Contains("msedge") && cmdLine.Contains("--inprivate") && title.Contains("inprivate"))
                    return true;

                // Firefox
                if (procName.Contains("firefox") && cmdLine.Contains("-private") && title.Contains("inprivate"))
                    return true;

                // Tor (always private)
                if (procName.Contains("tor"))
                    return true;

                // Fallback: window title check
                var windowTitleBuilder = new StringBuilder(256);
                GetWindowText(proc.MainWindowHandle, windowTitleBuilder, windowTitleBuilder.Capacity);
                string windowTitle = windowTitleBuilder.ToString().ToLower();

                if (windowTitle.Contains("incognito") ||
                    windowTitle.Contains("inprivate") ||
                    windowTitle.Contains("private browsing"))
                    return true;
            }
            catch
            {
                // ignore inaccessible processes
            }

            return false;
        }
        // Helper to get process command line
        private static string GetCommandLine(Process proc)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId={proc.Id}"))
                {
                    foreach (var obj in searcher.Get())
                        return (obj["CommandLine"] ?? "").ToString().ToLower();
                }
            }
            catch { }
            return "";
        }
        private static Process GetForegroundProcess()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return null;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return null;

            try
            {
                return Process.GetProcessById((int)pid);
            }
            catch
            {
                return null;
            }
        }




        // Known VPN keywords (can be expanded)
        private static readonly string[] vpnKeywords = new[]
        {
            "vpn", "nord", "express", "proton", "surfshark", "pia", "windscribe"
        };

        // Method to list all installed VPNs
        public static List<string> GetInstalledVPNs()
        {
            var vpnList = new List<string>();
            string[] registryRoots = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            RegistryKey[] hives = new[] { Registry.LocalMachine, Registry.CurrentUser };

            foreach (var hive in hives)
            {
                foreach (var keyPath in registryRoots)
                {
                    using (RegistryKey key = hive.OpenSubKey(keyPath))
                    {
                        if (key == null) continue;

                        foreach (string subkeyName in key.GetSubKeyNames())
                        {
                            using (RegistryKey subkey = key.OpenSubKey(subkeyName))
                            {
                                string displayName = subkey?.GetValue("DisplayName")?.ToString() ?? "";
                                if (string.IsNullOrWhiteSpace(displayName)) continue;

                                // Only include main VPNs, not helper apps
                                if (vpnKeywords.Any(k => displayName.ToLower() == k) && !vpnList.Contains(displayName))
                                    vpnList.Add(displayName);
                            }
                        }
                    }
                }
            }

            // Check Program Files folders
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var folder in new[] { programFiles, programFilesX86, localAppData })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var dir in Directory.GetDirectories(folder))
                {
                    string dirName = Path.GetFileName(dir).ToLower();
                    if (vpnKeywords.Any(k => dirName == k) && !vpnList.Contains(dirName))
                        vpnList.Add(Path.GetFileName(dir));
                }
            }

            return vpnList;
        }


    
    }
}
