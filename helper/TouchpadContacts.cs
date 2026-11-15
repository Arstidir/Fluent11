using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Fluent11Helper
{
    internal static class TouchpadContacts
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const int StaleMs = 1500;
        private const int ScrollFingers = 2;

        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_PREPARSEDDATA = 0x20000005;
        private const uint RIM_TYPEHID = 2;
        private const ushort DigitizerPage = 0x0D;
        private const ushort TouchPadUsage = 0x05;
        private const ushort TipSwitchUsage = 0x42;
        private const ushort ContactCountUsage = 0x54;
        private const int HidpInput = 0;
        private const int HidpStatusSuccess = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct USAGE_AND_PAGE
        {
            public ushort Usage;
            public ushort UsagePage;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

        [DllImport("hid.dll")]
        private static extern int HidP_GetUsagesEx(int reportType, ushort linkCollection, [Out] USAGE_AND_PAGE[] list, ref uint length,
            IntPtr preparsed, IntPtr report, uint reportLength);

        [DllImport("hid.dll")]
        private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value,
            IntPtr preparsed, IntPtr report, uint reportLength);

        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly Dictionary<IntPtr, IntPtr> preparsedData = new Dictionary<IntPtr, IntPtr>();
        private static readonly USAGE_AND_PAGE[] usages = new USAGE_AND_PAGE[64];
        private static IntPtr buffer;
        private static uint bufferSize;
        private static bool attempted;
        private static bool known;
        private static bool down;
        private static bool touching;
        private static double lastReport;

        internal static bool Known => known;

        internal static bool Down => down && clock.Elapsed.TotalMilliseconds - lastReport < StaleMs;

        internal static int Touches { get; private set; }

        internal static void EnsureRegistered()
        {
            if (attempted)
            {
                return;
            }

            try
            {
                var window = Application.Current?.MainWindow;
                var hwnd = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero || !(HwndSource.FromHwnd(hwnd) is HwndSource source))
                {
                    return;
                }

                attempted = true;
                var device = new RAWINPUTDEVICE { UsagePage = DigitizerPage, Usage = TouchPadUsage, Flags = 0, Target = hwnd };
                if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                {
                    logger.Warn($"Fluent 11: touchpad contacts not available (error {Marshal.GetLastWin32Error()}).");
                    return;
                }

                source.AddHook(WndProc);
                Application.Current.Deactivated += (s, e) => down = touching = false;
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: touchpad contacts not available.");
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUT)
            {
                try
                {
                    Read(lParam);
                }
                catch (Exception e)
                {
                    logger.Error(e, "Fluent 11: touchpad report not read.");
                }
            }

            return IntPtr.Zero;
        }

        private static void Read(IntPtr input)
        {
            var headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint size = 0;
            GetRawInputData(input, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0)
            {
                return;
            }

            if (size > bufferSize)
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                buffer = Marshal.AllocHGlobal((int)size);
                bufferSize = size;
            }

            if (GetRawInputData(input, RID_INPUT, buffer, ref size, headerSize) != size)
            {
                return;
            }

            var header = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RAWINPUTHEADER));
            var preparsed = header.Type == RIM_TYPEHID ? Preparsed(header.Device) : IntPtr.Zero;
            if (preparsed == IntPtr.Zero)
            {
                return;
            }

            var hid = IntPtr.Add(buffer, (int)headerSize);
            var reportSize = (uint)Marshal.ReadInt32(hid);
            var count = Marshal.ReadInt32(hid, 4);
            for (var i = 0; i < count; i++)
            {
                ReadReport(IntPtr.Add(hid, 8 + i * (int)reportSize), reportSize, preparsed);
            }
        }

        private static void ReadReport(IntPtr report, uint reportSize, IntPtr preparsed)
        {
            var hasCount = HidP_GetUsageValue(HidpInput, DigitizerPage, 0, ContactCountUsage, out var contacts, preparsed, report, reportSize) == HidpStatusSuccess;
            if (hasCount && contacts == 0)
            {
                return;
            }

            var length = (uint)usages.Length;
            if (HidP_GetUsagesEx(HidpInput, 0, usages, ref length, preparsed, report, reportSize) != HidpStatusSuccess)
            {
                return;
            }

            var tips = 0;
            for (var i = 0; i < length; i++)
            {
                if (usages[i].UsagePage == DigitizerPage && usages[i].Usage == TipSwitchUsage)
                {
                    tips++;
                }
            }

            var stale = clock.Elapsed.TotalMilliseconds - lastReport >= StaleMs;
            var wasTouching = touching && !stale;
            known = true;
            touching = tips > 0;
            down = tips >= ScrollFingers;
            lastReport = clock.Elapsed.TotalMilliseconds;
            if (touching && !wasTouching)
            {
                Touches++;
            }
        }

        private static IntPtr Preparsed(IntPtr device)
        {
            if (preparsedData.TryGetValue(device, out var data))
            {
                return data;
            }

            uint size = 0;
            GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
            data = IntPtr.Zero;
            if (size > 0)
            {
                data = Marshal.AllocHGlobal((int)size);
                if (GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, data, ref size) == unchecked((uint)-1))
                {
                    Marshal.FreeHGlobal(data);
                    data = IntPtr.Zero;
                }
            }

            preparsedData[device] = data;
            return data;
        }
    }
}
