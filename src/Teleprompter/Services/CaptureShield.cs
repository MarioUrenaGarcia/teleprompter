using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

namespace Teleprompter.Services;

public enum CaptureState
{
    /// <summary>La ventana no aparece en capturas ni al compartir pantalla.</summary>
    Hidden,

    /// <summary>Windows anterior a 10 version 2004: la ventana se ve como un recuadro negro.</summary>
    BlackedOut,

    Visible,
}

/// <summary>
/// Excluye de la captura de pantalla todas las ventanas de nivel superior del proceso.
/// Ademas de las ventanas propias, WPF y Windows crean ventanas independientes para tooltips,
/// listas desplegables, menus y dialogos de archivo; un gancho de eventos las detecta al crearse
/// para que tampoco se filtren al compartir pantalla.
/// </summary>
public static class CaptureShield
{
    private static readonly uint ProcessId = (uint)Environment.ProcessId;
    private static NativeMethods.WinEventProc? _callback;
    private static IntPtr _hook;
    private static bool _enabled;

    public static bool SupportsExclusion { get; } = Environment.OSVersion.Version.Build >= 19041;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            ApplyToAllWindows();
        }
    }

    /// <summary>Debe llamarse desde el hilo de interfaz: el gancho entrega los eventos por su cola de mensajes.</summary>
    public static void Initialize(bool enabled)
    {
        _enabled = enabled;
        if (_hook == IntPtr.Zero)
        {
            _callback = OnWinEvent;
            _hook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_CREATE,
                NativeMethods.EVENT_OBJECT_SHOW,
                IntPtr.Zero,
                _callback,
                ProcessId,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        ApplyToAllWindows();
    }

    public static void Shutdown()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            Apply(handle);
        }
    }

    public static CaptureState GetState(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowDisplayAffinity(handle, out var affinity))
        {
            return CaptureState.Visible;
        }

        return affinity switch
        {
            NativeMethods.WDA_EXCLUDEFROMCAPTURE => CaptureState.Hidden,
            NativeMethods.WDA_MONITOR => CaptureState.BlackedOut,
            _ => CaptureState.Visible,
        };
    }

    public static void ApplyToAllWindows()
    {
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ProcessId)
            {
                Apply(hwnd);
            }

            return true;
        }, IntPtr.Zero);
    }

    private static void Apply(IntPtr hwnd)
    {
        if (!_enabled)
        {
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_NONE);
            return;
        }

        if (SupportsExclusion && NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE))
        {
            return;
        }

        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_MONITOR);
    }

    private static void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (!_enabled || hwnd == IntPtr.Zero || idObject != NativeMethods.OBJID_WINDOW || idChild != 0)
        {
            return;
        }

        // La afinidad solo se puede asignar a ventanas de nivel superior; las hijas la heredan.
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
        {
            return;
        }

        try
        {
            Apply(hwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
