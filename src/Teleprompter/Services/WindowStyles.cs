using System.Runtime.InteropServices;

namespace Teleprompter.Services;

internal static class WindowStyles
{
    /// <summary>
    /// Ocupa el monitor completo donde esta la ventana, barra de tareas incluida. Se trabaja en pixeles
    /// fisicos con la API de Windows porque maximizar una ventana sin marco con WindowChrome la desborda
    /// unos pixeles por cada lado.
    /// </summary>
    public static bool CoverMonitor(IntPtr hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var bounds = info.Monitor;
        return NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Con WS_EX_TRANSPARENT sobre una ventana en capas, los clics pasan a la aplicacion de abajo,
    /// lo que permite trabajar detras del teleprompter sin moverlo.
    /// </summary>
    public static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        var updated = enabled
            ? style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED
            : style & ~NativeMethods.WS_EX_TRANSPARENT;

        if (updated != style)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, updated);
        }
    }
}
