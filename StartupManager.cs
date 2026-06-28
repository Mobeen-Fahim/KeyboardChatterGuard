using System;
using Microsoft.Win32;

namespace KeyboardChatterGuard
{
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "KeyboardChatterGuard";

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                if (key != null)
                {
                    object? val = key.GetValue(AppName);
                    return val != null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking startup: {ex.Message}");
            }
            return false;
        }

        public static void SetStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key != null)
                {
                    if (enable)
                    {
                        string? exePath = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            // Add --startup flag so we know to start minimized in tray
                            key.SetValue(AppName, $"\"{exePath}\" --startup");
                            SettingsManager.Instance.LaunchOnStartup = true;
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                        SettingsManager.Instance.LaunchOnStartup = false;
                    }
                    SettingsManager.Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting startup: {ex.Message}");
            }
        }
    }
}
