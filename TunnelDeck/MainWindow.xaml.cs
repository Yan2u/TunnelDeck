using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Text;
using System.Windows;
using WinRT.Interop;

namespace TunnelDeck
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Microsoft.UI.Windowing.AppWindow _appWindow;

        public MainWindow()
        {
            InitializeComponent();

            // set titlebar
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        }
    }
}