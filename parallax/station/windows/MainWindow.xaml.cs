using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Parallax.Station;

// A native Windows host; all product presentation lives in the React shell.
public sealed class MainWindow : Window
{
    internal StationWebHost WebHost { get; }
    public MainWindow(string? testDirectory = null) : this(testDirectory,null) { }
    internal MainWindow(string? testDirectory,StationRuntime? runtime)
    {
        Title = "PARALLAX STATION";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Parallax.png"));
        Width = 1260; Height = 820; MinWidth = 1040; MinHeight = 680;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(16,13,24));
        WebHost = new StationWebHost(testDirectory,runtime); Content = WebHost;
        Closed += (_, _) => WebHost.Dispose();
    }
}
