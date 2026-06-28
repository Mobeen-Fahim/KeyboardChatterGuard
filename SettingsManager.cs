using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KeyboardChatterGuard
{
    public class GhostKeyPair
    {
        public string TriggerKey { get; set; } = string.Empty;
        public string GhostKey { get; set; } = string.Empty;
        public int ThresholdMs { get; set; } = 40;
    }

    public class KeyboardProfile
    {
        public string KeyboardId { get; set; } = string.Empty;
        public string KeyboardName { get; set; } = string.Empty;
        public List<GhostKeyPair> GhostKeyPairs { get; set; } = new List<GhostKeyPair>();
        public Dictionary<string, int> KeyChatterOverrides { get; set; } = new Dictionary<string, int>();
        public int GlobalChatterThresholdMs { get; set; } = 50;
    }

    public class AppSettings
    {
        public bool IsFilterEnabled { get; set; } = true;
        public string SelectedKeyboardId { get; set; } = string.Empty; // VID_PID or full device path
        public string SelectedKeyboardName { get; set; } = string.Empty; // Friendly name
        public string PreferredKeyboardId { get; set; } = string.Empty; // Keyboard to auto-start
        public string PreferredKeyboardName { get; set; } = string.Empty;
        public int GlobalChatterThresholdMs { get; set; } = 50;
        public bool LaunchOnStartup { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public List<GhostKeyPair> GhostKeyPairs { get; set; } = new List<GhostKeyPair>();
        public Dictionary<string, int> KeyChatterOverrides { get; set; } = new Dictionary<string, int>();
        public List<KeyboardProfile> KeyboardProfiles { get; set; } = new List<KeyboardProfile>();

        public KeyboardProfile GetActiveProfile()
        {
            KeyboardProfiles ??= new List<KeyboardProfile>();
            string targetId = SelectedKeyboardId ?? string.Empty;

            var profile = KeyboardProfiles.Find(p => p.KeyboardId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                profile = new KeyboardProfile
                {
                    KeyboardId = targetId,
                    KeyboardName = string.IsNullOrEmpty(targetId) ? "Global Default Profile" : SelectedKeyboardName,
                    GlobalChatterThresholdMs = GlobalChatterThresholdMs,
                    GhostKeyPairs = new List<GhostKeyPair>(GhostKeyPairs ?? new List<GhostKeyPair>()),
                    KeyChatterOverrides = new Dictionary<string, int>(KeyChatterOverrides ?? new Dictionary<string, int>())
                };
                KeyboardProfiles.Add(profile);
            }

            profile.GhostKeyPairs ??= new List<GhostKeyPair>();
            profile.KeyChatterOverrides ??= new Dictionary<string, int>();
            return profile;
        }
    }

    public static class SettingsManager
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyboardChatterGuard"
        );
        private static readonly string ConfigPath = Path.Combine(AppDataFolder, "config.json");

        public static AppSettings Instance { get; private set; } = new AppSettings();

        static SettingsManager()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        Instance = settings;
                        // Ensure nested objects are initialized if null in JSON
                        Instance.GhostKeyPairs ??= new List<GhostKeyPair>();
                        Instance.KeyChatterOverrides ??= new Dictionary<string, int>();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            Instance = new AppSettings();
            Save();
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                string json = JsonSerializer.Serialize(Instance, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
