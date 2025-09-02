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

            string exePath = GetProcessPath(proc);
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
                { "chrome", "Google Chrome" },
                { "focustrack", "FocusTrack" },
                { "code", "Visual Studio Code" },
                { "devenv", "Visual Studio" },
                // Add more mappings as needed
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
