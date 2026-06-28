using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace KeyboardChatterGuard
{
    public class ConsoleLogItem
    {
        public string LogText { get; set; } = string.Empty;
        public Brush LogColor { get; set; } = Brushes.White;
    }

    public class OverrideListItem
    {
        public string KeyName { get; set; } = string.Empty;
        public int ThresholdMs { get; set; }
        public string ThresholdText => $"{ThresholdMs} ms";
    }

    public class GhostPairListItem
    {
        public string TriggerKey { get; set; } = string.Empty;
        public string GhostKey { get; set; } = string.Empty;
        public int ThresholdMs { get; set; }
        public string DelayText => $"Within {ThresholdMs} ms";
    }

    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ConsoleLogItem> _consoleLogs = new();
        private readonly ObservableCollection<KeyboardDevice> _keyboards = new();
        private readonly ObservableCollection<OverrideListItem> _overrides = new();
        private readonly ObservableCollection<GhostPairListItem> _ghostPairs = new();

        private int _totalChatterBlocked = 0;
        private int _totalGhostBlocked = 0;
        private bool _isRealClosing = false;

        public MainWindow()
        {
            InitializeComponent();

            // Set up collections
            LstConsoleLogs.ItemsSource = _consoleLogs;
            LstKeyboards.ItemsSource = _keyboards;
            LstOverrides.ItemsSource = _overrides;
            LstGhostPairs.ItemsSource = _ghostPairs;

            // Load saved settings into UI
            LoadSettingsIntoUi();

            // Hook up keyboard core callbacks
            KeyboardHook.Instance.KeyPressed += OnKeyPressed;
            KeyboardHook.Instance.ChatterBlocked += OnChatterBlocked;
            KeyboardHook.Instance.GhostBlocked += OnGhostBlocked;

            // Log startup
            AddLog("System", "KeyboardChatterGuard active and monitoring.", Brushes.Gray);

            // Initial refresh of hardware devices
            RefreshKeyboardsList();
            UpdateActiveStatus();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Hook window procedure to listen for USB device change messages
            var helper = new WindowInteropHelper(this);
            var hwndSource = HwndSource.FromHwnd(helper.Handle);
            hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == DeviceMonitor.WM_DEVICECHANGE)
            {
                int eventCode = wParam.ToInt32();
                if (eventCode == DeviceMonitor.DBT_DEVICEARRIVAL || eventCode == DeviceMonitor.DBT_DEVICEREMOVECOMPLETE)
                {
                    // USB devices changed, refresh list and update status
                    Dispatcher.Invoke(() =>
                    {
                        AddLog("USB Monitor", "USB Device configuration change detected.", Brushes.Gold);

                        var settings = SettingsManager.Instance;

                        // Auto-switch to preferred device on connect
                        if (eventCode == DeviceMonitor.DBT_DEVICEARRIVAL && !string.IsNullOrEmpty(settings.PreferredKeyboardId))
                        {
                            var connected = DeviceMonitor.GetConnectedKeyboards();
                            var pref = connected.FirstOrDefault(k => 
                                k.DevicePath.Equals(settings.PreferredKeyboardId, StringComparison.OrdinalIgnoreCase) || 
                                k.DisplayId.Equals(settings.PreferredKeyboardId, StringComparison.OrdinalIgnoreCase));
                            
                            if (pref != null && !settings.SelectedKeyboardId.Equals(pref.DevicePath, StringComparison.OrdinalIgnoreCase))
                            {
                                settings.SelectedKeyboardId = pref.DevicePath;
                                settings.SelectedKeyboardName = pref.Name;
                                SettingsManager.Save();
                                AddLog("Auto-Switch", $"Preferred keyboard '{pref.Name}' connected! Activated profile.", Brushes.LimeGreen);
                            }
                        }

                        // Auto-fallback to default on disconnect
                        if (eventCode == DeviceMonitor.DBT_DEVICEREMOVECOMPLETE)
                        {
                            var activeId = settings.SelectedKeyboardId;
                            if (!string.IsNullOrEmpty(activeId))
                            {
                                var connected = DeviceMonitor.GetConnectedKeyboards();
                                var isActiveStillConnected = connected.Any(k => 
                                    k.DevicePath.Equals(activeId, StringComparison.OrdinalIgnoreCase) || 
                                    k.DisplayId.Equals(activeId, StringComparison.OrdinalIgnoreCase));
                                
                                if (!isActiveStillConnected)
                                {
                                    settings.SelectedKeyboardId = string.Empty;
                                    settings.SelectedKeyboardName = "All Connected Keyboards (Global Profile)";
                                    SettingsManager.Save();
                                    AddLog("Auto-Switch", "Active keyboard unplugged. Switched to Global Profile.", Brushes.Orange);
                                }
                            }
                        }

                        RefreshKeyboardsList();
                        LoadSettingsIntoUi();
                        UpdateActiveStatus();
                    });
                }
            }
            return IntPtr.Zero;
        }

        private void LoadSettingsIntoUi()
        {
            var settings = SettingsManager.Instance;
            var profile = settings.GetActiveProfile();

            TglFilterActive.IsChecked = settings.IsFilterEnabled;
            UpdateFilterToggleUi(settings.IsFilterEnabled);

            SldGlobalThreshold.Value = profile.GlobalChatterThresholdMs;
            TxtGlobalThresholdValue.Text = $"{profile.GlobalChatterThresholdMs} ms";

            TglLaunchStartup.IsChecked = StartupManager.IsStartupEnabled();
            TglMinimizeTray.IsChecked = settings.MinimizeToTray;

            TxtActiveProfileHeader.Text = profile.KeyboardName;

            // Populate preferred keyboard ComboBox
            if (CboPreferredKeyboard != null)
            {
                CboPreferredKeyboard.SelectionChanged -= CboPreferredKeyboard_SelectionChanged;
                CboPreferredKeyboard.Items.Clear();

                CboPreferredKeyboard.Items.Add(new ComboBoxItem { Content = "None (Manual Selection)", Tag = string.Empty });

                var knownKeyboards = new List<KeyboardDevice>();
                
                // Add currently connected ones
                foreach (var k in _keyboards)
                {
                    if (!string.IsNullOrEmpty(k.DevicePath))
                    {
                        knownKeyboards.Add(k);
                    }
                }

                // Add any other profiles we have saved but not currently connected
                foreach (var prof in settings.KeyboardProfiles)
                {
                    if (!string.IsNullOrEmpty(prof.KeyboardId) && !knownKeyboards.Any(k => k.DevicePath == prof.KeyboardId))
                    {
                        knownKeyboards.Add(new KeyboardDevice
                        {
                            DevicePath = prof.KeyboardId,
                            Name = prof.KeyboardName,
                            Brand = "Generic",
                            IsConnected = false
                        });
                    }
                }

                int selectedIndex = 0;
                int index = 1;
                foreach (var k in knownKeyboards)
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{k.Brand} - {k.Name}",
                        Tag = k.DevicePath
                    };
                    CboPreferredKeyboard.Items.Add(item);
                    if (k.DevicePath.Equals(settings.PreferredKeyboardId, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = index;
                    }
                    index++;
                }

                CboPreferredKeyboard.SelectedIndex = selectedIndex;
                CboPreferredKeyboard.SelectionChanged += CboPreferredKeyboard_SelectionChanged;
            }

            RefreshOverridesList();
            RefreshGhostPairsList();
        }

        private void RefreshKeyboardsList()
        {
            _keyboards.Clear();
            var connected = DeviceMonitor.GetConnectedKeyboards();
            var selectedId = SettingsManager.Instance.SelectedKeyboardId;

            // Always add a "Global (All Keyboards)" option
            var defaultDev = new KeyboardDevice
            {
                DevicePath = string.Empty,
                Brand = "System",
                Name = "All Connected Keyboards (Global Profile)",
                VendorId = "0000",
                ProductId = "0000",
                IsConnected = true,
                IsActiveProfile = string.IsNullOrEmpty(selectedId)
            };
            defaultDev.InitializeBrandVisuals();
            _keyboards.Add(defaultDev);

            foreach (var k in connected)
            {
                k.IsActiveProfile = k.DevicePath.Equals(selectedId, StringComparison.OrdinalIgnoreCase) || 
                                     k.DisplayId.Equals(selectedId, StringComparison.OrdinalIgnoreCase);
                _keyboards.Add(k);
            }
        }

        private void UpdateActiveStatus()
        {
            var settings = SettingsManager.Instance;
            var profile = settings.GetActiveProfile();
            bool isEnabled = settings.IsFilterEnabled;
            string selectedId = settings.SelectedKeyboardId;

            if (TxtFooterStatus != null)
            {
                TxtFooterStatus.Text = $"v1.0.0 • Profile: {profile.KeyboardName}";
            }

            bool isDeviceConnected = true;

            if (isEnabled && !string.IsNullOrEmpty(selectedId))
            {
                // Verify if the configured hardware profile keyboard is actually connected
                isDeviceConnected = _keyboards.Any(k => 
                    !string.IsNullOrEmpty(k.DevicePath) && 
                    (k.DevicePath.Equals(selectedId, StringComparison.OrdinalIgnoreCase) || 
                     k.DisplayId.Equals(selectedId, StringComparison.OrdinalIgnoreCase)));

                if (!isDeviceConnected)
                {
                    // Temporarily suspend hook processing because the designated device is unplugged
                    KeyboardHook.Instance.Stop();
                    TxtStatusLabel.Text = "SUSPENDED (UNPLUGGED)";
                    TxtStatusLabel.Foreground = Brushes.Crimson;
                    App.TrayManager?.UpdateTrayIconStatus(false);
                    return;
                }
            }

            if (isEnabled)
            {
                KeyboardHook.Instance.Start();
                KeyboardHook.Instance.UpdateSettings();
                TxtStatusLabel.Text = "ACTIVE";
                TxtStatusLabel.Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
                App.TrayManager?.UpdateTrayIconStatus(true);
            }
            else
            {
                KeyboardHook.Instance.Stop();
                TxtStatusLabel.Text = "PAUSED";
                TxtStatusLabel.Foreground = Brushes.DarkGray;
                App.TrayManager?.UpdateTrayIconStatus(false);
            }
        }

        public void UpdateFilterToggleUi(bool active)
        {
            TglFilterActive.IsChecked = active;
            UpdateActiveStatus();
        }

        // ================= KEYBOARD EVENTS (HOOK) =================

        private void OnKeyPressed(string keyName, bool isDown)
        {
            Dispatcher.Invoke(() =>
            {
                if (isDown)
                {
                    AddLog("Input", $"Key Down: {keyName}", Brushes.LightGreen);
                }
            });
        }

        private void OnChatterBlocked(string keyName, int elapsedMs)
        {
            Dispatcher.Invoke(() =>
            {
                _totalChatterBlocked++;
                TxtChatterBlockedCount.Text = _totalChatterBlocked.ToString();
                AddLog("Chatter Guard", $"Blocked repeat on '{keyName}' ({elapsedMs}ms elapsed)", Brushes.Tomato);
            });
        }

        private void OnGhostBlocked(string triggerKey, string ghostKey, int elapsedMs)
        {
            Dispatcher.Invoke(() =>
            {
                _totalGhostBlocked++;
                TxtGhostBlockedCount.Text = _totalGhostBlocked.ToString();
                AddLog("Ghost Guard", $"Blocked '{ghostKey}' triggered by '{triggerKey}' within {elapsedMs}ms", new SolidColorBrush(Color.FromRgb(255, 75, 145)));
            });
        }

        // ================= CONSOLE LOGGER =================

        private void AddLog(string module, string text, Brush color)
        {
            string timeStamp = DateTime.Now.ToString("HH:mm:ss.ff");
            _consoleLogs.Add(new ConsoleLogItem
            {
                LogText = $"[{timeStamp}] [{module}] {text}",
                LogColor = color
            });

            // Prevent scroll/memory bloat
            if (_consoleLogs.Count > 120)
            {
                _consoleLogs.RemoveAt(0);
            }

            // Scroll to end
            ConsoleScrollViewer.ScrollToEnd();
        }

        private void ClearConsole_Click(object sender, RoutedEventArgs e)
        {
            _consoleLogs.Clear();
            AddLog("System", "Console logs cleared.", Brushes.Gray);
        }

        // ================= NAVIGATION =================

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string target = btn.Tag.ToString() ?? "Dashboard";

            TxtHeaderTitle.Text = target switch
            {
                "Dashboard" => "Dashboard Status & Ghost Key Fix",
                "Devices" => "Keyboard Profiles",
                "Chatter" => "Chatter Filtering Settings",
                "Settings" => "Application Settings & System Logs",
                _ => target
            };

            // Toggle visibilities
            TabDashboard.Visibility = target == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
            TabDevices.Visibility = target == "Devices" ? Visibility.Visible : Visibility.Collapsed;
            TabChatter.Visibility = target == "Chatter" ? Visibility.Visible : Visibility.Collapsed;
            TabSettings.Visibility = target == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================= CHATTER TAB LOGIC =================

        private void SldGlobalThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtGlobalThresholdValue == null) return;

            int val = (int)e.NewValue;
            TxtGlobalThresholdValue.Text = $"{val} ms";

            var profile = SettingsManager.Instance.GetActiveProfile();
            profile.GlobalChatterThresholdMs = val;
            SettingsManager.Save();
            KeyboardHook.Instance.UpdateSettings();
        }

        private void RefreshOverridesList()
        {
            _overrides.Clear();
            var profile = SettingsManager.Instance.GetActiveProfile();
            foreach (var kvp in profile.KeyChatterOverrides)
            {
                _overrides.Add(new OverrideListItem { KeyName = kvp.Key, ThresholdMs = kvp.Value });
            }
        }

        private void AddOverride_Click(object sender, RoutedEventArgs e)
        {
            string key = TxtOverrideKey.Text.Trim();
            if (string.IsNullOrEmpty(key)) return;

            if (int.TryParse(TxtOverrideMs.Text, out int ms) && ms > 0)
            {
                var profile = SettingsManager.Instance.GetActiveProfile();
                profile.KeyChatterOverrides[key] = ms;
                SettingsManager.Save();
                KeyboardHook.Instance.UpdateSettings();

                RefreshOverridesList();

                TxtOverrideKey.Clear();
                TxtOverrideMs.Clear();
                AddLog("Config", $"Added override for key '{key}' at {ms}ms", Brushes.Aqua);
            }
        }

        private void DeleteOverride_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is OverrideListItem item)
            {
                var profile = SettingsManager.Instance.GetActiveProfile();
                profile.KeyChatterOverrides.Remove(item.KeyName);
                SettingsManager.Save();
                KeyboardHook.Instance.UpdateSettings();
                RefreshOverridesList();
                AddLog("Config", $"Removed override for key '{item.KeyName}'", Brushes.Aqua);
            }
        }

        // ================= GHOST TAB LOGIC =================

        private void RefreshGhostPairsList()
        {
            _ghostPairs.Clear();
            var profile = SettingsManager.Instance.GetActiveProfile();
            foreach (var pair in profile.GhostKeyPairs)
            {
                _ghostPairs.Add(new GhostPairListItem 
                { 
                    TriggerKey = pair.TriggerKey, 
                    GhostKey = pair.GhostKey, 
                    ThresholdMs = pair.ThresholdMs 
                });
            }
        }

        private void AddGhostPair_Click(object sender, RoutedEventArgs e)
        {
            string trigger = TxtGhostTrigger.Text.Trim().ToUpper();
            string ghost = TxtGhostTarget.Text.Trim().ToUpper();
            
            if (string.IsNullOrEmpty(trigger) || string.IsNullOrEmpty(ghost)) return;

            if (int.TryParse(TxtGhostDelay.Text, out int delay) && delay > 0)
            {
                var profile = SettingsManager.Instance.GetActiveProfile();
                // Ensure no duplicates
                profile.GhostKeyPairs.RemoveAll(p => 
                    p.TriggerKey.Equals(trigger, StringComparison.OrdinalIgnoreCase) && 
                    p.GhostKey.Equals(ghost, StringComparison.OrdinalIgnoreCase));

                profile.GhostKeyPairs.Add(new GhostKeyPair
                {
                    TriggerKey = trigger,
                    GhostKey = ghost,
                    ThresholdMs = delay
                });

                SettingsManager.Save();
                KeyboardHook.Instance.UpdateSettings();
                
                RefreshGhostPairsList();
                
                TxtGhostTrigger.Clear();
                TxtGhostTarget.Clear();
                AddLog("Config", $"Added Ghost rule: [{trigger}] triggers [{ghost}] within {delay}ms", Brushes.Aqua);
            }
        }

        private void DeleteGhostPair_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GhostPairListItem item)
            {
                var profile = SettingsManager.Instance.GetActiveProfile();
                profile.GhostKeyPairs.RemoveAll(p => 
                    p.TriggerKey.Equals(item.TriggerKey, StringComparison.OrdinalIgnoreCase) && 
                    p.GhostKey.Equals(item.GhostKey, StringComparison.OrdinalIgnoreCase));

                SettingsManager.Save();
                KeyboardHook.Instance.UpdateSettings();
                RefreshGhostPairsList();
                AddLog("Config", $"Removed Ghost rule: [{item.TriggerKey}] ➔ [{item.GhostKey}]", Brushes.Aqua);
            }
        }

        // ================= KEYBOARD VIEW LOGIC =================

        private void RefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            RefreshKeyboardsList();
            UpdateActiveStatus();
        }

        private void SelectDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                string devicePath = btn.Tag.ToString() ?? "";
                
                // Find device name
                var dev = _keyboards.FirstOrDefault(k => k.DevicePath == devicePath);
                string name = dev != null ? dev.Name : "All Connected Keyboards (Global Profile)";

                SettingsManager.Instance.SelectedKeyboardId = devicePath;
                SettingsManager.Instance.SelectedKeyboardName = name;
                SettingsManager.Save();

                AddLog("Profile", $"Switched active hardware filter to: {name}", Brushes.Aqua);
                RefreshKeyboardsList();
                LoadSettingsIntoUi();
                UpdateActiveStatus();
            }
        }

        // ================= SETTINGS TAB LOGIC =================

        private void TglFilterActive_Checked(object sender, RoutedEventArgs e)
        {
            SettingsManager.Instance.IsFilterEnabled = true;
            SettingsManager.Save();
            UpdateActiveStatus();
        }

        private void TglFilterActive_Unchecked(object sender, RoutedEventArgs e)
        {
            SettingsManager.Instance.IsFilterEnabled = false;
            SettingsManager.Save();
            UpdateActiveStatus();
        }

        private void TglLaunchStartup_Checked(object sender, RoutedEventArgs e)
        {
            StartupManager.SetStartup(true);
            AddLog("Startup", "Run on Windows Startup enabled.", Brushes.Aqua);
        }

        private void TglLaunchStartup_Unchecked(object sender, RoutedEventArgs e)
        {
            StartupManager.SetStartup(false);
            AddLog("Startup", "Run on Windows Startup disabled.", Brushes.Aqua);
        }

        private void TglMinimizeTray_Checked(object sender, RoutedEventArgs e)
        {
            SettingsManager.Instance.MinimizeToTray = true;
            SettingsManager.Save();
        }

        private void TglMinimizeTray_Unchecked(object sender, RoutedEventArgs e)
        {
            SettingsManager.Instance.MinimizeToTray = false;
            SettingsManager.Save();
        }

        private void ResetConfig_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to restore defaults? All key limits and ghost pairs will be wiped.", "Reset Configuration", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                SettingsManager.Instance.IsFilterEnabled = true;
                SettingsManager.Instance.SelectedKeyboardId = string.Empty;
                SettingsManager.Instance.SelectedKeyboardName = "All Connected Keyboards";
                SettingsManager.Instance.GlobalChatterThresholdMs = 50;
                SettingsManager.Instance.GhostKeyPairs.Clear();
                SettingsManager.Instance.KeyChatterOverrides.Clear();
                SettingsManager.Save();

                LoadSettingsIntoUi();
                UpdateActiveStatus();

                AddLog("Config", "Configuration restored to defaults.", Brushes.Aqua);
            }
        }

        // ================= WINDOW MANAGEMENT & TITLE BAR =================

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch { }
            }
        }

        private void MinBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isRealClosing && SettingsManager.Instance.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                AddLog("System", "Minimized to tray. Double click icon to open.", Brushes.Gray);
            }
            base.OnClosing(e);
        }

        public void RealShutdown()
        {
            _isRealClosing = true;
            Close();
        }

        // ================= VALIDATIONS =================

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Allow numbers only
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void CboPreferredKeyboard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboPreferredKeyboard.SelectedItem is ComboBoxItem item)
            {
                string id = item.Tag?.ToString() ?? string.Empty;
                string name = item.Content?.ToString() ?? string.Empty;

                var settings = SettingsManager.Instance;
                settings.PreferredKeyboardId = id;
                settings.PreferredKeyboardName = id == string.Empty ? string.Empty : name;
                SettingsManager.Save();

                AddLog("Preferred Device", id == string.Empty ? "Disabled auto-switch preferred keyboard." : $"Preferred keyboard set to: {name}", Brushes.Aqua);
            }
        }

        private void TxtOverrideKey_KeyDown(object sender, KeyEventArgs e)
        {
            // Enter sets value
            if (e.Key == Key.Enter)
            {
                AddOverride_Click(sender, new RoutedEventArgs());
            }
        }
    }
}
