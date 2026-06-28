using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace KeyboardChatterGuard
{
    public class KeyboardDevice
    {
        public string DevicePath { get; set; } = string.Empty;
        public string VendorId { get; set; } = string.Empty; // Hex string e.g. 1532
        public string ProductId { get; set; } = string.Empty; // Hex string e.g. 0253
        public string Name { get; set; } = string.Empty;
        public string Brand { get; set; } = "Generic";
        public bool IsConnected { get; set; } = true;

        public bool IsActiveProfile { get; set; } = false;

        public string BrandColor { get; set; } = "#8A8A9E";
        public string AccentColor { get; set; } = "#4FACFE";
        public string BrandLogoData { get; set; } = string.Empty;

        private System.Windows.Media.Brush? _brandColorBrush;
        public System.Windows.Media.Brush BrandColorBrush
        {
            get
            {
                if (_brandColorBrush == null)
                {
                    try
                    {
                        _brandColorBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(BrandColor);
                    }
                    catch
                    {
                        _brandColorBrush = System.Windows.Media.Brushes.SlateGray;
                    }
                }
                return _brandColorBrush;
            }
        }

        public string DisplayId => $"USB\\VID_{VendorId}&PID_{ProductId}";
        public string DisplayName => $"{Brand} - {Name} ({VendorId}:{ProductId})";

        public void InitializeBrandVisuals()
        {
            const string pathGeneric = "M2,6 H30 V22 H2 V6 Z M5,9 H8 V12 H5 Z M10,9 H13 V12 H10 Z M15,9 H18 V12 H15 Z M20,9 H23 V12 H20 Z M25,9 H28 V12 H25 Z M5,14 H28 V19 H5 Z";
            const string pathRazer = "M 4 10 C 4 7, 7 4, 12 4 C 17 4, 20 7, 20 10 C 20 13, 16 15, 14 17 L 14 19 L 10 19 L 10 17 C 8 15, 4 13, 4 10 Z";
            const string pathLogitech = "M 12 3 A 9 9 0 1 0 21 12 L 21 10 L 13 10 L 13 14 L 17 14 A 5 5 0 1 1 12 7 A 5 5 0 0 1 15 8 L 18 5 A 9 9 0 0 0 12 3 Z";
            const string pathCorsair = "M 4 20 L 7 5 L 14 10 L 4 20 M 8 20 L 11 8 L 18 12 L 8 20 M 12 20 L 15 11 L 20 14 L 12 20";
            const string pathSteelSeries = "M 12 2 A 10 10 0 1 0 22 12 A 10 10 0 0 0 12 2 M 12 6 A 6 6 0 1 1 6 12 A 6 6 0 0 1 12 6 M 12 9 A 3 3 0 1 0 15 12 A 3 3 0 0 0 12 9";
            const string pathHyperX = "M 4 4 L 8 4 L 8 10 L 16 10 L 16 4 L 20 4 L 20 20 L 16 20 L 16 14 L 8 14 L 8 20 L 4 20 Z";

            switch (Brand.ToLowerInvariant())
            {
                case "razer":
                    BrandColor = "#33FF33";
                    AccentColor = "#00F2FE";
                    BrandLogoData = pathRazer;
                    break;
                case "logitech":
                    BrandColor = "#00B5E2";
                    AccentColor = "#4FACFE";
                    BrandLogoData = pathLogitech;
                    break;
                case "corsair":
                    BrandColor = "#FFCC00";
                    AccentColor = "#FF007F";
                    BrandLogoData = pathCorsair;
                    break;
                case "steelseries":
                    BrandColor = "#FF5500";
                    AccentColor = "#FFD700";
                    BrandLogoData = pathSteelSeries;
                    break;
                case "hyperx":
                case "hyperx/hp":
                    BrandColor = "#FF003C";
                    AccentColor = "#8A00FF";
                    BrandLogoData = pathHyperX;
                    break;
                case "system":
                    BrandColor = "#00F2FE";
                    AccentColor = "#4FACFE";
                    BrandLogoData = pathGeneric;
                    break;
                default:
                    BrandColor = "#8A8A9E";
                    AccentColor = "#4FACFE";
                    BrandLogoData = pathGeneric;
                    break;
            }
        }
    }

    public static class DeviceMonitor
    {
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIM_TYPEKEYBOARD = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceList(
            [Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
            ref uint puiNumDevices,
            uint cbSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetRawInputDeviceInfo(
            IntPtr hDevice,
            uint uiCommand,
            [Out] StringBuilder? pbData,
            ref uint pcbSize);

        // Windows WM_DEVICECHANGE constants
        public const int WM_DEVICECHANGE = 0x0219;
        public const int DBT_DEVICEARRIVAL = 0x8000;
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        private static readonly Dictionary<string, string> BrandRegistry = new(StringComparer.OrdinalIgnoreCase)
        {
            { "1532", "Razer" },
            { "046D", "Logitech" },
            { "1B1C", "Corsair" },
            { "1038", "SteelSeries" },
            { "0951", "HyperX" }, // Kingston HyperX
            { "03F0", "HyperX/HP" }, // HP HyperX / HP keyboards
            { "3434", "Keychron" },
            { "31E3", "Wooting" },
            { "0A81", "Ducky" },
            { "22D4", "Glorious" },
            { "3151", "Epomaker" },
            { "1689", "Varmilo" },
            { "0B05", "ASUS ROG" },
            { "1462", "MSI" },
            { "05AC", "Apple" },
            { "045E", "Microsoft" },
            { "413C", "Dell" },
            { "17EF", "Lenovo" },
            { "04F2", "Chicony" },
            { "093A", "PixArt" },
            { "258A", "Varmilo/Custom" }
        };

        public static List<KeyboardDevice> GetConnectedKeyboards()
        {
            var keyboards = new List<KeyboardDevice>();
            uint numDevices = 0;
            uint cbSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();

            // First call gets the count
            if (GetRawInputDeviceList(null, ref numDevices, cbSize) != 0 || numDevices == 0)
            {
                return keyboards;
            }

            var deviceList = new RAWINPUTDEVICELIST[numDevices];
            if (GetRawInputDeviceList(deviceList, ref numDevices, cbSize) == uint.MaxValue)
            {
                return keyboards;
            }

            var uniqueMap = new Dictionary<string, KeyboardDevice>(StringComparer.OrdinalIgnoreCase);

            foreach (var dev in deviceList)
            {
                if (dev.dwType != RIM_TYPEKEYBOARD)
                    continue;

                uint nameSize = 0;
                // First call gets name buffer size
                GetRawInputDeviceInfo(dev.hDevice, RIDI_DEVICENAME, null, ref nameSize);

                if (nameSize == 0)
                    continue;

                var nameSb = new StringBuilder((int)nameSize);
                if (GetRawInputDeviceInfo(dev.hDevice, RIDI_DEVICENAME, nameSb, ref nameSize) > 0)
                {
                    string devicePath = nameSb.ToString();
                    if (string.IsNullOrEmpty(devicePath))
                        continue;

                    var parsed = ParseDevicePath(devicePath);
                    if (parsed == null)
                        continue;

                    string key = $"{parsed.VendorId}:{parsed.ProductId}";
                    if (!uniqueMap.ContainsKey(key))
                    {
                        var name = QueryDeviceNameFromRegistry(devicePath);
                        var brand = LookupBrand(parsed.VendorId, name);
                        var devInfo = new KeyboardDevice
                        {
                            DevicePath = devicePath,
                            VendorId = parsed.VendorId,
                            ProductId = parsed.ProductId,
                            Name = name,
                            Brand = brand,
                            IsConnected = true
                        };
                        devInfo.InitializeBrandVisuals();
                        uniqueMap[key] = devInfo;
                    }
                }
            }

            return uniqueMap.Values.ToList();
        }

        private class TempDeviceData
        {
            public string VendorId { get; set; } = string.Empty;
            public string ProductId { get; set; } = string.Empty;
        }

        private static TempDeviceData? ParseDevicePath(string path)
        {
            // Path looks like: \\?\HID#VID_1532&PID_0253&MI_00#7&1f2d34a&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}
            try
            {
                string cleanPath = path;
                if (cleanPath.StartsWith(@"\\?\"))
                {
                    cleanPath = cleanPath.Substring(4);
                }

                string[] parts = cleanPath.Split('#');
                if (parts.Length < 2)
                    return null;

                string vidPidPart = parts[1]; // e.g. VID_1532&PID_0253&MI_00
                string vid = "";
                string pid = "";

                int vidIndex = vidPidPart.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
                if (vidIndex >= 0 && vidIndex + 8 <= vidPidPart.Length)
                {
                    vid = vidPidPart.Substring(vidIndex + 4, 4);
                }

                int pidIndex = vidPidPart.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
                if (pidIndex >= 0 && pidIndex + 8 <= vidPidPart.Length)
                {
                    pid = vidPidPart.Substring(pidIndex + 4, 4);
                }

                if (!string.IsNullOrEmpty(vid) && !string.IsNullOrEmpty(pid))
                {
                    return new TempDeviceData
                    {
                        VendorId = vid.ToUpperInvariant(),
                        ProductId = pid.ToUpperInvariant()
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse device path {path}: {ex.Message}");
            }

            return null;
        }

        private static string QueryDeviceNameFromRegistry(string devicePath)
        {
            try
            {
                // Format path for registry: HID#VID_1532&PID_0253&MI_00#7&1f2d34a&0&0000
                string cleanPath = devicePath;
                if (cleanPath.StartsWith(@"\\?\"))
                {
                    cleanPath = cleanPath.Substring(4);
                }

                // Strip trailing GUID if present e.g. #{884b96c3-...}
                int guidIndex = cleanPath.IndexOf("}");
                if (guidIndex > 0)
                {
                    int lastHash = cleanPath.LastIndexOf('#', guidIndex);
                    if (lastHash > 0)
                    {
                        cleanPath = cleanPath.Substring(0, lastHash);
                    }
                }

                string[] parts = cleanPath.Split('#');
                if (parts.Length < 3)
                    return "Keyboard Device";

                string category = parts[0]; // e.g. HID
                string vidPid = parts[1];   // e.g. VID_1532&PID_0253&MI_00
                string instance = parts[2]; // e.g. 7&1f2d34a&0&0000

                string regPath = $@"SYSTEM\CurrentControlSet\Enum\{category}\{vidPid}\{instance}";
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key != null)
                {
                    // Check for FriendlyName first
                    object? friendly = key.GetValue("FriendlyName");
                    if (friendly != null && !string.IsNullOrWhiteSpace(friendly.ToString()))
                    {
                        return friendly.ToString()!.Trim();
                    }

                    // Fallback to DeviceDesc
                    object? desc = key.GetValue("DeviceDesc");
                    if (desc != null)
                    {
                        string descStr = desc.ToString()!;
                        // Often looks like: "@keyboard.inf,%keyboard.keyboard%;Standard PS/2 Keyboard" or "Razer Cynosa V2;Keyboard Device"
                        int semiIndex = descStr.LastIndexOf(';');
                        if (semiIndex >= 0 && semiIndex + 1 < descStr.Length)
                        {
                            descStr = descStr.Substring(semiIndex + 1);
                        }
                        
                        // Strip localized string syntax e.g. @oem12.inf,%devicedesc%;...
                        if (descStr.StartsWith("@"))
                        {
                            int lastSemi = descStr.LastIndexOf(';');
                            if (lastSemi >= 0)
                            {
                                descStr = descStr.Substring(lastSemi + 1);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(descStr))
                        {
                            return descStr.Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Registry query error: {ex.Message}");
            }

            return "Keyboard Device";
        }

        private static string LookupBrand(string vid, string deviceName)
        {
            if (BrandRegistry.TryGetValue(vid, out string? brand))
            {
                return brand;
            }

            // Fallback: search within the device name
            foreach (var kvp in BrandRegistry)
            {
                if (deviceName.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return "Generic";
        }
    }
}
