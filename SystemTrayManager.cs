using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace KeyboardChatterGuard
{
    public class SystemTrayManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _toggleMenuItem;
        private readonly Window _mainWindow;

        private Icon? _activeIcon;
        private Icon? _inactiveIcon;

        public SystemTrayManager(Window mainWindow)
        {
            _mainWindow = mainWindow;
            
            CreateIcons();

            _contextMenu = new ContextMenuStrip();
            
            var openItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => ShowWindow());
            openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
            _contextMenu.Items.Add(openItem);

            _toggleMenuItem = new ToolStripMenuItem("Filtering Enabled", null, (s, e) => ToggleFiltering());
            _toggleMenuItem.Checked = SettingsManager.Instance.IsFilterEnabled;
            _contextMenu.Items.Add(_toggleMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());
            _contextMenu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = SettingsManager.Instance.IsFilterEnabled ? _activeIcon : _inactiveIcon,
                Text = "KeyboardChatterGuard (Active)",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };

            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        }

        private void CreateIcons()
        {
            // Dynamically draw a clean shield/badge icon in memory
            _activeIcon = CreateDynamicIcon(Color.FromArgb(0, 242, 254)); // Cyber Cyan
            _inactiveIcon = CreateDynamicIcon(Color.FromArgb(120, 120, 130)); // Inactive Muted Gray
        }

        private Icon CreateDynamicIcon(Color accentColor)
        {
            using var bitmap = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Background shield/base shape (dark slate gray)
            using var bgBrush = new SolidBrush(Color.FromArgb(20, 20, 28));
            g.FillEllipse(bgBrush, 2, 2, 28, 28);

            // Ring border
            using var borderPen = new Pen(Color.FromArgb(45, 45, 60), 2);
            g.DrawEllipse(borderPen, 3, 3, 26, 26);

            // Inner glowing dot/keyboard icon
            using var accentBrush = new SolidBrush(accentColor);
            
            // Draw a simplified mini-keyboard grid/shield badge
            // Let's draw 3 small rounded keycaps
            g.FillRectangle(accentBrush, 8, 12, 4, 4);
            g.FillRectangle(accentBrush, 14, 12, 4, 4);
            g.FillRectangle(accentBrush, 20, 12, 4, 4);
            g.FillRectangle(accentBrush, 8, 18, 16, 4); // Spacebar

            IntPtr hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        public void UpdateTrayIconStatus(bool enabled)
        {
            _toggleMenuItem.Checked = enabled;
            _notifyIcon.Icon = enabled ? _activeIcon : _inactiveIcon;

            string activeKeyboard = SettingsManager.Instance.SelectedKeyboardName;
            if (string.IsNullOrEmpty(activeKeyboard) || activeKeyboard.Contains("Global Profile", StringComparison.OrdinalIgnoreCase))
            {
                activeKeyboard = "Global Default";
            }
            else
            {
                if (activeKeyboard.Length > 20) activeKeyboard = activeKeyboard.Substring(0, 17) + "...";
            }

            string statusText = enabled ? $"Active - {activeKeyboard}" : $"Disabled - {activeKeyboard}";
            if (statusText.Length > 63) statusText = statusText.Substring(0, 60) + "...";

            _notifyIcon.Text = $"KeyboardChatterGuard ({statusText})";
        }

        private void ToggleFiltering()
        {
            bool nextState = !SettingsManager.Instance.IsFilterEnabled;
            SettingsManager.Instance.IsFilterEnabled = nextState;
            SettingsManager.Save();

            KeyboardHook.Instance.UpdateSettings();
            UpdateTrayIconStatus(nextState);

            // Notify UI if open
            if (_mainWindow is MainWindow main)
            {
                main.UpdateFilterToggleUi(nextState);
            }
        }

        private void ShowWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void ExitApplication()
        {
            StopServices();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void StopServices()
        {
            KeyboardHook.Instance.Stop();
        }

        public void Dispose()
        {
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _activeIcon?.Dispose();
            _inactiveIcon?.Dispose();
        }
    }
}
