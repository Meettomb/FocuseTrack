using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // List of known system/host processes to ignore
        private static readonly HashSet<string> BlockedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
        "ApplicationFrameHost",
        "SearchHost",
        "dfsvc",
        "explorer",
        "svchost",
        "RuntimeBroker",
        "System",
        "Idle"
        };


        public static (string AppName, string Title, string ExePath) GetActiveWindowInfo()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ("", "", "");

            GetWindowThreadProcessId(hwnd, out uint pid);

            Process proc = null;
            try
            {
                proc = Process.GetProcessById((int)pid);
            }
            catch
            {
                return ("Unknown", "", "");
            }

            // Ignore system/host processes
            if (BlockedProcesses.Contains(proc.ProcessName))
                return ("", "", "");

            string exePath = GetProcessPath(proc);

            // Ignore processes running from Windows system folder
            if (string.IsNullOrWhiteSpace(exePath) ||
                exePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
            {
                return ("", "", "");
            }

            string appName = GetFriendlyAppName(proc, proc.ProcessName);

            // Get window title
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();

            return (appName, title, exePath);
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

        private static string GetFriendlyAppName(Process proc, string processName)
        {
            var appNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Browsers
                { "chrome", "Google Chrome" },
                { "msedge", "Microsoft Edge" },
                { "firefox", "Mozilla Firefox" },
                { "opera", "Opera Browser" },
                { "iexplore", "Internet Explorer" },

                // Development tools
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

                // Microsoft Office
                { "winword", "Microsoft Word" },
                { "excel", "Microsoft Excel" },
                { "powerpnt", "Microsoft PowerPoint" },
                { "outlook", "Microsoft Outlook" },
                { "onenote", "Microsoft OneNote" },
                { "access", "Microsoft Access" },

                // Communication & Collaboration
                { "teams", "Microsoft Teams" },
                { "zoom", "Zoom" },
                { "slack", "Slack" },
                { "discord", "Discord" },
                { "skype", "Skype" },
                { "telegram", "Telegram" },
                { "whatsapp", "WhatsApp" },

                // Media Players
                { "spotify", "Spotify" },
                { "vlc", "VLC Media Player" },
                { "wmplayer", "Windows Media Player" },
                { "itunes", "iTunes" },
                { "foobar2000", "Foobar2000" },

                // Adobe Creative Suite
                { "photoshop", "Adobe Photoshop" },
                { "illustrator", "Adobe Illustrator" },
                { "afterfx", "Adobe After Effects" },
                { "premiere", "Adobe Premiere Pro" },
                { "audition", "Adobe Audition" },
                { "lightroom", "Adobe Lightroom" },
                { "acrobat", "Adobe Acrobat Reader" },
                { "animate", "Adobe Animate" },

                // Graphics & 3D
                { "blender", "Blender" },
                { "unity", "Unity Editor" },
                { "unrealengine", "Unreal Engine" },
                { "maya", "Autodesk Maya" },
                { "3dsmax", "3ds Max" },

                // Utilities
                { "paint", "Paint" },
                { "calc", "Calculator" },
                { "cmd", "Command Prompt" },
                { "powershell", "PowerShell" },
                { "explorer", "File Explorer" },

                // Gaming Platforms
                { "steam", "Steam" },
                { "epicgameslauncher", "Epic Games Launcher" },
                { "origin", "Origin" },
                { "battle.net", "Battle.net" },
                { "gog", "GOG Galaxy" },

                // Misc Popular Apps
                { "onenote", "Microsoft OneNote" },
                { "teamspeak3", "TeamSpeak 3" },
                { "obs64", "OBS Studio" },
                { "zoom", "Zoom" },
                { "notion", "Notion" },
                { "postman", "Postman" },
                { "filezilla", "FileZilla" },
                { "gitkraken", "GitKraken" }
            };


            if (appNameMap.TryGetValue(processName, out var friendlyName))
                return friendlyName;

            try
            {
                var productName = proc?.MainModule?.FileVersionInfo?.ProductName;
                if (!string.IsNullOrWhiteSpace(productName))
                    return productName;
            }
            catch { }

            return processName;
        }
    }
}
