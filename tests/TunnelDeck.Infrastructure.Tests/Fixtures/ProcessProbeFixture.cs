using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TunnelDeck.Infrastructure.Tests.Fixtures;

internal sealed class ProcessProbeFixture : IDisposable
{
    private readonly StorageFixture _storage = new();
    public string Root { get; }
    public string Executable => Path.Combine(Root, "TunnelDeck.ProcessProbe.exe");

    public ProcessProbeFixture()
    {
        Root = Path.Combine(_storage.Directory.RootPath, "argv receiver 开发");
        Directory.CreateDirectory(Root);
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ProcessProbe")))
        { File.Copy(file, Path.Combine(Root, Path.GetFileName(file))); }
    }

    public async Task<string[]> ReadArgumentsAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string path = Path.Combine(Root, "argv.json");
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try
            {
                using JsonDocument result = JsonDocument.Parse(await File.ReadAllTextAsync(path, deadline.Token));
                return result.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            { await Task.Delay(30, deadline.Token); }
        }
    }

    public static void CloseTestWindow(string title)
    {
        EnumWindows((handle, _) =>
        {
            var text = new StringBuilder(512);
            GetWindowText(handle, text, text.Capacity);
            if (text.ToString().Equals(title, StringComparison.Ordinal)) { PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero); }
            return true;
        }, IntPtr.Zero);
    }

    public void Dispose()
    {
        // The receiver writes immediately before normal exit. Wait for its executable mapping to close.
        for (int attempt = 0; ; attempt++)
        {
            try { _storage.Dispose(); return; }
            catch (IOException) when (attempt < 40) { Thread.Sleep(25); }
        }
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
