using System;
using System.Threading;
using System.Windows;

namespace KeyboardChatterGuard
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        public static SystemTrayManager? TrayManager { get; private set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 1. Single Instance Protection
            _mutex = new Mutex(true, "KeyboardChatterGuard-Mutex-Unique", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("KeyboardChatterGuard is already running in the background.", "KeyboardChatterGuard", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // 2. Start global hook
            KeyboardHook.Instance.Start();

            // 3. Process startup flags
            bool startMinimized = false;
            foreach (string arg in e.Args)
            {
                if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                    break;
                }
            }

            // 4. Configure background running behavior
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 5. Create main window
            var mainWindow = new MainWindow();
            
            // 6. Start System Tray Manager
            TrayManager = new SystemTrayManager(mainWindow);

            if (!startMinimized)
            {
                mainWindow.Show();
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            KeyboardHook.Instance.Stop();
            TrayManager?.Dispose();
            
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
                _mutex.Dispose();
            }
        }
    }
}
