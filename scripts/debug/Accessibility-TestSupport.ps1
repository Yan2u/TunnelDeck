$ErrorActionPreference = 'Stop'
if (-not ('TunnelDeck.Verification.AccessibilityNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TunnelDeck.Verification {
    public static class AccessibilityNative {
        [StructLayout(LayoutKind.Sequential)] private struct HighContrast { public uint Size, Flags; public IntPtr Scheme; }
        public sealed class ContrastSnapshot { public uint Flags; public string Scheme; public bool Enabled => (Flags & 1) != 0; }
        [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SystemParametersInfoW", SetLastError=true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, ref HighContrast data, uint update);
        [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SendMessageTimeoutW", SetLastError=true)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr value, string area, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        public static uint ForegroundProcess() { GetWindowThreadProcessId(GetForegroundWindow(), out uint process); return process; }
        public static void RestoreIfMinimized(IntPtr window) { if (IsIconic(window)) ShowWindowAsync(window, 9); }
        public static void NotifyTextScaleChanged() {
            SendMessageTimeout(new IntPtr(0xffff), 0x1a, IntPtr.Zero, "Accessibility", 2, 2000, out _);
        }
        public static ContrastSnapshot ReadContrast() {
            var state = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
            if (!SystemParametersInfo(0x42, state.Size, ref state, 0)) throw new InvalidOperationException("Cannot read the system contrast setting.");
            return new ContrastSnapshot { Flags = state.Flags, Scheme = Marshal.PtrToStringUni(state.Scheme) };
        }
        public static void SetContrast(ContrastSnapshot original, bool enabled) {
            if ((original.Flags & 2) == 0) throw new InvalidOperationException("High contrast is not available on this system.");
            IntPtr scheme = Marshal.StringToHGlobalUni(original.Scheme);
            try {
                var state = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>(), Flags = enabled ? original.Flags | 1u : original.Flags & ~1u, Scheme = scheme };
                // Request a runtime change. Windows can initialize a previously empty default scheme name.
                if (!SystemParametersInfo(0x43, state.Size, ref state, 2)) throw new InvalidOperationException("Cannot apply the system contrast setting.");
            }
            finally { Marshal.FreeHGlobal(scheme); }
        }
    }
}
'@
}
