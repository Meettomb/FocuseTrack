using FocusTrack.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;                // for File.Exists
using System.Management;
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

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

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
            "lsass"
        };

        // Map for known UWP apps running under ApplicationFrameHost
        private static readonly Dictionary<string, (string FriendlyName, string IconPath)> UwpApps =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "WhatsApp", ("WhatsApp", "Assets/Icons/whatsapp.png") },
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
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ("", "", "", null);

            GetWindowThreadProcessId(hwnd, out uint pid);

            Process proc = null;
            try
            {
                proc = Process.GetProcessById((int)pid);
            }
            catch
            {
                Debug.WriteLine($"[GetActiveWindowInfo] Failed to get process for PID {pid}");
                return ("Unknown", "", "", null);
            }

            // Ignore unwanted system processes
            if (IgnoredProcesses.Contains(proc.ProcessName))
                return ("", "", "", null);

            string exePath = GetProcessPath(proc);
            Debug.WriteLine($"[GetActiveWindowInfo] Process: {proc.ProcessName}, Path: {exePath}");

            // Get window title
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            string windowTitle = sb.ToString();
            Debug.WriteLine($"[GetActiveWindowInfo] Window Title: {windowTitle}");

            // === Add this block here ===
            if (proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            {
                // Skip if no window title (user not actively using file explorer)
                if (string.IsNullOrWhiteSpace(windowTitle) || windowTitle == "Program Manager")
                    return ("", "", "", null);
            }
            


            // Extract app icon
            byte[] appIcon = IconHelper.GetIconBytes(exePath);
            if (appIcon != null && appIcon.Length > 0)
                Debug.WriteLine($"[GetActiveWindowInfo] Extracted icon for {exePath}, size = {appIcon.Length} bytes");
            else
                Debug.WriteLine($"[GetActiveWindowInfo] Failed to extract icon for {exePath}");

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
                                if (appIcon != null)
                                    Debug.WriteLine($"Used fallback icon for {kvp.Value.FriendlyName}, size={appIcon.Length} bytes");
                            }

                            return (kvp.Value.FriendlyName, windowTitle, exePath, appIcon);
                        }
                    }
                }
            }


            string appName = GetFriendlyAppName(proc, proc.ProcessName);
            Debug.WriteLine($"[GetActiveWindowInfo] Final app name: {appName}");

            return (appName, windowTitle, exePath, appIcon);
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
                { "foobar2000", "Foobar2000" }
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




    }
}
