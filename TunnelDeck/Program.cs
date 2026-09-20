using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TunnelDeck.Services;

namespace TunnelDeck;

internal static class Program
{
    internal static AppLaunchOptions Launch { get; private set; } = null!;
    internal static SingleInstanceService Instance { get; private set; } = null!;

    [STAThread]
    private static int Main(string[] arguments)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        try
        {
            Launch = AppLaunchOptions.Parse(arguments);
            using var instance = new SingleInstanceService(Launch.Directory.RootPath);
            if (!instance.IsPrimary) { instance.Redirect(); return 0; }
            Instance = instance;
            Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new App();
            });
            return 0;
        }
        catch (Exception error)
        {
            MessageBox(IntPtr.Zero, $"TunnelDeck could not start. Check the application installation and data directory.\n\nError code: 0x{error.HResult:X8}", "TunnelDeck", 0x10);
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr window, string text, string title, uint options);
}
