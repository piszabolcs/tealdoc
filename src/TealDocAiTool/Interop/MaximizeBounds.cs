using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TealDocAiTool.Interop;

/// <summary>
/// WindowChrome-os ablak maximalizálva alapból túllóg a képernyőn a méretezési keret
/// vastagságával. A WM_GETMINMAXINFO megválaszolásával az ablakot az AKTUÁLIS monitor
/// munkaterületéhez igazítjuk — ez a fix margónál helyesebb, mert követi a monitorváltást,
/// az eltérő DPI-t és a tálca tetszőleges pozícióját is.
///
/// Mivel az üzenetet mi kezeljük (handled = true), a WPF saját kezelője nem fut le — így a
/// MinWidth/MinHeight érvényesítése (ptMinTrackSize) is a mi dolgunk. Korábban ez kimaradt, és az
/// ablak a megengedett minimum alá is lekicsinyíthető volt (C-18).
/// </summary>
public static class MaximizeBounds
{
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x0002;

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(window)!;
            source.AddHook((nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
                Hook(window, hwnd, msg, lParam, ref handled));
        };
    }

    /// <summary>A minimális ablakméret fizikai pixelben (DIP × DPI-skála, felfelé kerekítve).</summary>
    public static (int Width, int Height) MinTrackSize(double minWidthDip, double minHeightDip, double dpiScaleX, double dpiScaleY) =>
        ((int)Math.Ceiling(minWidthDip * dpiScaleX), (int)Math.Ceiling(minHeightDip * dpiScaleY));

    private static nint Hook(Window window, nint hwnd, int msg, nint lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return nint.Zero;

        nint monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero) return nint.Zero;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return nint.Zero;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.x = info.rcWork.left - info.rcMonitor.left;
        mmi.ptMaxPosition.y = info.rcWork.top - info.rcMonitor.top;
        mmi.ptMaxSize.x = info.rcWork.right - info.rcWork.left;
        mmi.ptMaxSize.y = info.rcWork.bottom - info.rcWork.top;

        var dpi = VisualTreeHelper.GetDpi(window);
        var (minW, minH) = MinTrackSize(window.MinWidth, window.MinHeight, dpi.DpiScaleX, dpi.DpiScaleY);
        mmi.ptMinTrackSize.x = Math.Max(mmi.ptMinTrackSize.x, minW);
        mmi.ptMinTrackSize.y = Math.Max(mmi.ptMinTrackSize.y, minH);
        Marshal.StructureToPtr(mmi, lParam, true);

        handled = true;
        return nint.Zero;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
