using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Parallax.Station;

internal static class DpiChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left,Top,Right,Bottom; }
    private delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref Bounds bounds,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Bounds bounds);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);

    internal static async Task Run(MainWindow window,StationWebHost host,string page,string output)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(!AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(handle),new IntPtr(-4)))throw new InvalidOperationException("Station is not per-monitor V2 DPI aware.");
        if(!GetWindowRect(handle,out var original))throw new InvalidOperationException("Window bounds unavailable for display scaling check.");
        var monitors=new List<Bounds>();
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr monitor,IntPtr dc,ref Bounds bounds,IntPtr data)=>{monitors.Add(bounds);return true;},IntPtr.Zero);
        var results=new List<object>();
        try
        {
            foreach(var monitor in monitors.Take(3))
            {
                if(!SetWindowPos(handle,IntPtr.Zero,monitor.Left+32,monitor.Top+32,0,0,0x15))throw new InvalidOperationException("Could not move diagnostic window between displays.");
                bool matched=false;
                for(int attempt=0;attempt<60;attempt++)
                {
                    await Task.Delay(100);
                    var density=VisualTreeHelper.GetDpi(host);var bounds=host.PageBounds(page);
                    using var capture=new MemoryStream();await host.PageCore(page).CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,capture);capture.Position=0;
                    var image=BitmapDecoder.Create(capture,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];
                    int width=(int)Math.Round(bounds.Width*density.DpiScaleX),height=(int)Math.Round(bounds.Height*density.DpiScaleY);
                    if(Math.Abs(image.PixelWidth-width)>1 || Math.Abs(image.PixelHeight-height)>1)continue;
                    var frame=JsonSerializer.SerializeToElement(host.PageFrameMetrics(page));
                    if(frame.GetProperty("pixelWidth").ValueKind==JsonValueKind.Number && Math.Abs(frame.GetProperty("pixelWidth").GetInt32()-width)>1)continue;
                    results.Add(new{dpi=density.DpiScaleX,renderedWidth=image.PixelWidth,renderedHeight=image.PixelHeight,displayWidth=width,displayHeight=height,frame});
                    matched=true;break;
                }
                if(!matched)throw new InvalidOperationException("The browser did not match the display density after moving between monitors.");
            }
        }
        finally
        {
            SetWindowPos(handle,IntPtr.Zero,original.Left,original.Top,original.Right-original.Left,original.Bottom-original.Top,0x14);
            await Task.Delay(500);
            File.WriteAllText(Path.Combine(output,"display-scaling.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
