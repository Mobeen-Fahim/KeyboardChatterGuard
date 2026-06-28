using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KeyboardChatterGuard
{
    public class KeyboardHook
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        private class PendingEvent
        {
            public uint VkCode { get; set; }
            public uint ScanCode { get; set; }
            public uint Flags { get; set; }
            public string KeyName { get; set; } = string.Empty;
            public long Timestamp { get; set; }
            public bool IsKeyDown { get; set; }
            public bool Discarded { get; set; }
        }

        private readonly List<PendingEvent> _pendingEvents = new();
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;
        private bool _isInjecting = false;

        // State tracking
        private readonly Dictionary<uint, long> _lastKeyDownTimes = new();
        private readonly Dictionary<string, long> _lastAnyKeyDownTimes = new();
        
        // Settings cached for fast, lock-free access
        private bool _isEnabled = true;
        private int _globalThresholdMs = 50;
        private readonly Dictionary<string, int> _keyOverrides = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<GhostKeyPair> _ghostPairs = new();

        // Events
        public event Action<string, int>? ChatterBlocked;
        public event Action<string, string, int>? GhostBlocked;
        public event Action<string, bool>? KeyPressed; // (KeyName, isDown)

        private static KeyboardHook? _instance;
        public static KeyboardHook Instance => _instance ??= new KeyboardHook();

        private KeyboardHook()
        {
            // Cache initial settings
            UpdateSettings();
        }

        public void Start()
        {
            if (_hookId != IntPtr.Zero) return;

            _proc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule != null && curModule.ModuleName != null)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        public void Stop()
        {
            if (_hookId == IntPtr.Zero) return;

            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _proc = null;
        }

        public void UpdateSettings()
        {
            var settings = SettingsManager.Instance;
            _isEnabled = settings.IsFilterEnabled;

            var profile = settings.GetActiveProfile();
            _globalThresholdMs = profile.GlobalChatterThresholdMs;

            lock (_keyOverrides)
            {
                _keyOverrides.Clear();
                foreach (var kvp in profile.KeyChatterOverrides)
                {
                    _keyOverrides[kvp.Key] = kvp.Value;
                }
            }

            lock (_ghostPairs)
            {
                _ghostPairs.Clear();
                _ghostPairs.AddRange(profile.GhostKeyPairs);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (_isInjecting)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (nCode >= 0)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = (int)wParam;
                bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                string keyName = GetKeyFriendlyName(kb.vkCode);

                if (isKeyDown || isKeyUp)
                {
                    // Raise key event for UI live visualizer (safely in background or dispatched)
                    KeyPressed?.Invoke(keyName, isKeyDown);
                }

                if (_isEnabled)
                {
                    long now = Environment.TickCount64;

                    // A. Ghost Key Filtering & Queuing
                    if (isKeyDown || isKeyUp)
                    {
                        GhostKeyPair? matchingPair = null;
                        lock (_ghostPairs)
                        {
                            foreach (var pair in _ghostPairs)
                            {
                                if (string.Equals(pair.GhostKey, keyName, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchingPair = pair;
                                    break;
                                }
                            }
                        }

                        if (matchingPair != null)
                        {
                            // 1. If it's a Down event, check if Trigger was pressed recently (Forward Ghosting)
                            if (isKeyDown)
                            {
                                if (_lastAnyKeyDownTimes.TryGetValue(matchingPair.TriggerKey, out long lastTriggerTime))
                                {
                                    long elapsed = now - lastTriggerTime;
                                    if (elapsed <= matchingPair.ThresholdMs)
                                    {
                                        // Block immediately!
                                        GhostBlocked?.Invoke(matchingPair.TriggerKey, matchingPair.GhostKey, (int)elapsed);
                                        return (IntPtr)1;
                                    }
                                }
                            }

                            // 2. Queue for delay to check if Trigger is about to be pressed shortly (Reverse Ghosting)
                            var ev = new PendingEvent
                            {
                                VkCode = kb.vkCode,
                                ScanCode = kb.scanCode,
                                Flags = kb.flags,
                                KeyName = keyName,
                                Timestamp = now,
                                IsKeyDown = isKeyDown,
                                Discarded = false
                            };

                            lock (_pendingEvents)
                            {
                                _pendingEvents.Add(ev);
                            }

                            Task.Run(async () =>
                            {
                                await Task.Delay(matchingPair.ThresholdMs);
                                lock (_pendingEvents)
                                {
                                    if (!ev.Discarded)
                                    {
                                        _pendingEvents.Remove(ev);
                                        if (ev.IsKeyDown)
                                        {
                                            _lastKeyDownTimes[ev.VkCode] = Environment.TickCount64;
                                            _lastAnyKeyDownTimes[ev.KeyName] = Environment.TickCount64;
                                        }
                                        InjectKeyEvent((ushort)ev.VkCode, (ushort)ev.ScanCode, ev.IsKeyDown, ev.Flags);
                                    }
                                }
                            });

                            return (IntPtr)1; // Consume key (does not appear on screen yet)
                        }

                        // 3. Check if this key is a Trigger Key in any rule (Reverse Ghosting check)
                        if (isKeyDown)
                        {
                            GhostKeyPair? triggerRule = null;
                            lock (_ghostPairs)
                            {
                                foreach (var pair in _ghostPairs)
                                {
                                    if (string.Equals(pair.TriggerKey, keyName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        triggerRule = pair;
                                        break;
                                    }
                                }
                            }

                            if (triggerRule != null)
                            {
                                lock (_pendingEvents)
                                {
                                    var matched = _pendingEvents
                                        .Where(e => string.Equals(e.KeyName, triggerRule.GhostKey, StringComparison.OrdinalIgnoreCase))
                                        .ToList();

                                    if (matched.Any())
                                    {
                                        foreach (var ev in matched)
                                        {
                                            ev.Discarded = true;
                                            _pendingEvents.Remove(ev);
                                        }

                                        long firstGhostTime = matched[0].Timestamp;
                                        GhostBlocked?.Invoke(triggerRule.TriggerKey, triggerRule.GhostKey, (int)(now - firstGhostTime));
                                    }
                                }
                            }
                        }
                    }

                    // B. Key Chatter (Debounce) - Down events only
                    if (isKeyDown)
                    {
                        int threshold = _globalThresholdMs;
                        lock (_keyOverrides)
                        {
                            if (_keyOverrides.TryGetValue(keyName, out int customThreshold))
                            {
                                threshold = customThreshold;
                            }
                        }

                        if (_lastKeyDownTimes.TryGetValue(kb.vkCode, out long lastDownTime))
                        {
                            long elapsed = now - lastDownTime;
                            if (elapsed < threshold)
                            {
                                ChatterBlocked?.Invoke(keyName, (int)elapsed);
                                return (IntPtr)1; // Consume key
                            }
                        }

                        // Key is allowed. Record timestamps
                        _lastKeyDownTimes[kb.vkCode] = now;
                        _lastAnyKeyDownTimes[keyName] = now;
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void InjectKeyEvent(ushort vkCode, ushort scanCode, bool isKeyDown, uint originalFlags)
        {
            _isInjecting = true;
            try
            {
                uint dwFlags = KEYEVENTF_SCANCODE;
                if (!isKeyDown)
                {
                    dwFlags |= KEYEVENTF_KEYUP;
                }
                if ((originalFlags & 1) != 0)
                {
                    dwFlags |= KEYEVENTF_EXTENDEDKEY;
                }

                INPUT[] inputs = new INPUT[1];
                inputs[0] = new INPUT { type = INPUT_KEYBOARD };
                inputs[0].ki = new KEYBDINPUT
                {
                    wVk = vkCode,
                    wScan = scanCode,
                    dwFlags = dwFlags,
                    time = 0,
                    dwExtraInfo = GetMessageExtraInfo()
                };

                uint result = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
                if (result == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"SendInput failed. Result: {result}, Error: {err}, Size: {Marshal.SizeOf<INPUT>()}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Key injection failed: {ex.Message}");
            }
            finally
            {
                _isInjecting = false;
            }
        }

        public static string GetKeyFriendlyName(uint vkCode)
        {
            try
            {
                Key key = KeyInterop.KeyFromVirtualKey((int)vkCode);
                string name = key.ToString();

                // Clean up default enum names to make them super user friendly
                if (name.Length == 2 && name.StartsWith('D'))
                {
                    return name.Substring(1); // D0 -> 0, D1 -> 1, etc.
                }

                return name switch
                {
                    "LeftShift" => "LShift",
                    "RightShift" => "RShift",
                    "LeftCtrl" => "LCtrl",
                    "RightCtrl" => "RCtrl",
                    "LeftAlt" => "LAlt",
                    "RightAlt" => "RAlt",
                    "LWin" => "Win",
                    "RWin" => "Win",
                    "Capital" => "CapsLock",
                    "Return" => "Enter",
                    "Back" => "Backspace",
                    "Escape" => "Esc",
                    "OemQuestion" => "?",
                    "OemQuotes" => "\"",
                    "OemSemicolon" => ";",
                    "OemOpenBrackets" => "[",
                    "OemCloseBrackets" => "]",
                    "OemPipe" => "|",
                    "OemComma" => ",",
                    "OemPeriod" => ".",
                    "OemMinus" => "-",
                    "OemPlus" => "+",
                    "OemTilde" => "`",
                    "Divide" => "/",
                    "Multiply" => "*",
                    "Subtract" => "-",
                    "Add" => "+",
                    "Decimal" => ".",
                    _ => name
                };
            }
            catch
            {
                return $"VK_{vkCode}";
            }
        }
    }
}
