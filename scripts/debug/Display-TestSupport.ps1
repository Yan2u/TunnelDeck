$ErrorActionPreference = 'Stop'
if (-not ('TunnelDeck.Verification.DisplayNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace TunnelDeck.Verification {
    public static class DisplayNative {
        public sealed class Monitor { public string Device; public int X, Y, Width, Height; public uint Dpi; }
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rectangle, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct MonitorInfo {
            public int Size; public Rect Monitor, Work; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Device;
        }
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extended, string type, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        public static bool IsPerMonitorV2(IntPtr window) => AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(window), new IntPtr(-4));
        public static Monitor[] Monitors() {
            var result = new List<Monitor>();
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rectangle, data) => {
                    var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                    if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Cannot read the monitor work area.");
                    IntPtr probe = CreateWindowEx(0, "STATIC", "", 0x80000000, info.Work.Left + 10, info.Work.Top + 10, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (probe == IntPtr.Zero) throw new InvalidOperationException("Cannot create an invisible DPI probe.");
                    try { result.Add(new Monitor { Device = info.Device, X = info.Work.Left, Y = info.Work.Top, Width = info.Work.Right - info.Work.Left, Height = info.Work.Bottom - info.Work.Top, Dpi = GetDpiForWindow(probe) }); }
                    finally { DestroyWindow(probe); }
                    return true;
                }, IntPtr.Zero);
            }
            finally { SetThreadDpiAwarenessContext(previous); }
            return result.ToArray();
        }
        public static void MoveToMonitor(IntPtr window, Monitor monitor, int logicalWidth, int logicalHeight) {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try {
                int width = Math.Min((int)(logicalWidth * monitor.Dpi / 96.0), monitor.Width - 24);
                int height = Math.Min((int)(logicalHeight * monitor.Dpi / 96.0), monitor.Height - 24);
                if (!SetWindowPos(window, IntPtr.Zero, monitor.X + 12, monitor.Y + 12, width, height, 4)) throw new InvalidOperationException("Cannot move the test window to the requested monitor.");
            }
            finally { SetThreadDpiAwarenessContext(previous); }
        }
    }
}
'@
}
