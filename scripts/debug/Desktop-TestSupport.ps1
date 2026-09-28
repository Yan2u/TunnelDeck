$ErrorActionPreference = 'Stop'
if (-not ('TunnelDeck.Verification.ForwardNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
namespace TunnelDeck.Verification
{
    public static class ForwardNative
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attaching);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags; [FieldOffset(8)] public int MouseX; [FieldOffset(12)] public int MouseY; [FieldOffset(20)] public uint MouseFlags; }
        private static void RequireForeground(IntPtr window)
        {
            GetWindowThreadProcessId(window, out uint target);
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
            if (target != foreground)
            {
                SetForegroundWindow(window);
                for (int attempt = 0; attempt < 30 && target != foreground; attempt++)
                { Thread.Sleep(10); GetWindowThreadProcessId(GetForegroundWindow(), out foreground); }
            }
            if (target != 0 && target != foreground)
            {
                // A driver started from a background shell may not own foreground rights yet. Attach to the
                // current foreground thread's input queue for the switch; the ownership check below still applies.
                IntPtr current = GetForegroundWindow();
                uint foregroundThread = GetWindowThreadProcessId(current, out _);
                uint thisThread = GetCurrentThreadId();
                bool attached = foregroundThread != 0 && foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);
                try
                {
                    var release = new[] { new Input { Type = 1, Key = 0x10, Flags = 2 } };
                    SendInput(1, release, Marshal.SizeOf<Input>());
                    BringWindowToTop(window);
                    SetForegroundWindow(window);
                }
                finally { if (attached) { AttachThreadInput(thisThread, foregroundThread, false); } }
                for (int attempt = 0; attempt < 30 && target != foreground; attempt++)
                { Thread.Sleep(10); GetWindowThreadProcessId(GetForegroundWindow(), out foreground); }
            }
            if (target == 0 || target != foreground) { throw new InvalidOperationException("Keyboard input requires this test app in the foreground."); }
        }
        public static void Activate(IntPtr window) => RequireForeground(window);
        public static void Press(IntPtr window, ushort key, ushort modifier = 0)
        {
            RequireForeground(window);
            Input Down(ushort value) => new Input { Type = 1, Key = value };
            Input Up(ushort value) => new Input { Type = 1, Key = value, Flags = 2 };
            Input[] input = modifier == 0 ? new[] { Down(key), Up(key) } : new[] { Down(modifier), Down(key), Up(key), Up(modifier) };
            if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) != input.Length) { throw new InvalidOperationException("Could not send test keyboard input."); }
        }
        public static void Drag(IntPtr window, int fromX, int fromY, int toX, int toY)
        {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            bool pressed = false;
            try
            {
                if (!GetWindowRect(window, out Rect bounds) || fromX <= bounds.Left || fromX >= bounds.Right || toX <= bounds.Left || toX >= bounds.Right ||
                    fromY <= bounds.Top || fromY >= bounds.Bottom || toY <= bounds.Top || toY >= bounds.Bottom)
                { throw new InvalidOperationException("Drag coordinates must be observed inside the test window."); }
                RequireForeground(window);
                Thread.Sleep(100);
                if (!SetCursorPos(fromX, fromY)) { throw new InvalidOperationException("Could not position the test pointer."); }
                var down = new[] { new Input { Type = 0, MouseFlags = 2 } };
                if (SendInput(1, down, Marshal.SizeOf<Input>()) != 1) { throw new InvalidOperationException("Could not start the test drag."); }
                pressed = true;
                Thread.Sleep(300);
                if ((GetAsyncKeyState(1) & 0x8000) == 0) { throw new InvalidOperationException("The test drag lost its pressed mouse button."); }
                for (int step = 1; step <= 24; step++)
                {
                    // Re-activating a window during native drag/drop can cancel the gesture.
                    // A transient null foreground is allowed; a different app still aborts it.
                    GetWindowThreadProcessId(window, out uint targetProcess);
                    GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcess);
                    if (targetProcess == 0 || foregroundProcess != 0 && foregroundProcess != targetProcess)
                    { throw new InvalidOperationException($"The drag was interrupted by foreground process {foregroundProcess}; test process {targetProcess}."); }
                    int x = fromX + (toX - fromX) * step / 24, y = fromY + (toY - fromY) * step / 24;
                    var move = new[] { new Input { Type = 0, MouseFlags = 0xC001,
                        MouseX = (int)((long)(x - GetSystemMetrics(76)) * 65535 / (GetSystemMetrics(78) - 1)),
                        MouseY = (int)((long)(y - GetSystemMetrics(77)) * 65535 / (GetSystemMetrics(79) - 1)) } };
                    if (SendInput(1, move, Marshal.SizeOf<Input>()) != 1) { throw new InvalidOperationException("Could not move the test pointer."); }
                    Thread.Sleep(20);
                }
                Thread.Sleep(150);
                if (!GetCursorPos(out Point actual) || Math.Abs(actual.X - toX) > 3 || Math.Abs(actual.Y - toY) > 3)
                { throw new InvalidOperationException($"The pointer moved away from the test drop target ({toX}, {toY}); actual ({actual.X}, {actual.Y})."); }
            }
            finally
            {
                if (pressed) { SendInput(1, new[] { new Input { Type = 0, MouseFlags = 4 } }, Marshal.SizeOf<Input>()); }
                SetThreadDpiAwarenessContext(previous);
            }
        }
        public static void Resize(IntPtr window, int width, int height)
        {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { double scale = GetDpiForWindow(window) / 96.0; if (!SetWindowPos(window, IntPtr.Zero, 0, 0, (int)(width * scale), (int)(height * scale), 6)) { throw new InvalidOperationException("Resize failed."); } }
            finally { SetThreadDpiAwarenessContext(previous); }
        }
        public static async Task AssertPayloadAsync(int port, byte[] payload)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            var stream = client.GetStream();
            var actual = new byte[payload.Length];
            await Task.WhenAll(stream.WriteAsync(payload, timeout.Token).AsTask(), stream.ReadExactlyAsync(actual, timeout.Token).AsTask());
            if (!payload.AsSpan().SequenceEqual(actual)) { throw new InvalidOperationException("Forwarded payload was changed."); }
        }
        public static void AssertReusable(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = false };
            try { listener.Start(); } finally { listener.Stop(); }
        }
    }
    public sealed class ForwardPortBlock : IDisposable
    {
        private readonly TcpListener[] _listeners;
        public int First { get; }
        public ForwardPortBlock(int count)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                int first = Random.Shared.Next(20000, 29000 - count);
                var listeners = new TcpListener[count];
                try
                {
                    for (int index = 0; index < count; index++)
                    { listeners[index] = new TcpListener(IPAddress.Loopback, first + index) { ExclusiveAddressUse = false }; listeners[index].Start(); }
                    First = first; _listeners = listeners; return;
                }
                catch (SocketException) { foreach (var listener in listeners) { listener?.Stop(); } }
            }
            throw new InvalidOperationException("No isolated test port block was available.");
        }
        public void Release(int index) => _listeners[index].Stop();
        public void Dispose() { foreach (var listener in _listeners) { listener.Stop(); } }
    }
    public sealed class ForwardEchoServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly ConcurrentBag<Task> _clients = new ConcurrentBag<Task>();
        private readonly Task _accept;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public ForwardEchoServer() { _listener.Start(); _accept = AcceptAsync(); }
        private async Task AcceptAsync()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _clients.Add(EchoAsync(client)); } }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task EchoAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var bytes = new byte[65536]; var stream = client.GetStream(); int count;
                    while ((count = await stream.ReadAsync(bytes, _stop.Token)) > 0) { await stream.WriteAsync(bytes.AsMemory(0, count), _stop.Token); }
                }
                catch (OperationCanceledException) { }
                catch (System.IO.IOException) { }
            }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _accept.GetAwaiter().GetResult(); Task.WhenAll(_clients).GetAwaiter().GetResult(); _stop.Dispose(); }
    }
}
'@
}
