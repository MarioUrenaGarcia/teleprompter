using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Teleprompter.Services;

public enum HotkeyAction
{
    TogglePlay,
    Faster,
    Slower,
    ScrollBack,
    ScrollForward,
    Restart,
    ToggleVisibility,
    ToggleClickThrough,
}

public sealed record HotkeyDefinition(HotkeyAction Action, ModifierKeys Modifiers, Key Key, bool Repeat, string Description)
{
    public string Display => HotkeyText.Format(Modifiers, Key);
}

public static class HotkeyCatalog
{
    private const ModifierKeys CtrlAlt = ModifierKeys.Control | ModifierKeys.Alt;

    public static IReadOnlyList<HotkeyDefinition> All { get; } =
    [
        new(HotkeyAction.TogglePlay, CtrlAlt, Key.Space, false, "Iniciar o pausar"),
        new(HotkeyAction.Faster, CtrlAlt, Key.Up, true, "Aumentar velocidad"),
        new(HotkeyAction.Slower, CtrlAlt, Key.Down, true, "Reducir velocidad"),
        new(HotkeyAction.ScrollBack, CtrlAlt, Key.PageUp, true, "Retroceder el texto"),
        new(HotkeyAction.ScrollForward, CtrlAlt, Key.PageDown, true, "Adelantar el texto"),
        new(HotkeyAction.Restart, CtrlAlt, Key.Home, false, "Volver al inicio"),
        new(HotkeyAction.ToggleVisibility, CtrlAlt, Key.H, false, "Mostrar u ocultar el teleprompter"),
        new(HotkeyAction.ToggleClickThrough, CtrlAlt, Key.T, false, "Dejar pasar los clics o volver a usar el ratón"),
    ];
}

public static class HotkeyText
{
    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Mayús");
        }

        parts.Add(key switch
        {
            Key.Space => "Espacio",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Left => "←",
            Key.Right => "→",
            Key.PageUp => "RePág",
            Key.PageDown => "AvPág",
            Key.Home => "Inicio",
            Key.End => "Fin",
            _ => key.ToString(),
        });

        return string.Join(" + ", parts);
    }
}

/// <summary>Atajos de teclado que funcionan aunque otra aplicacion (por ejemplo la videollamada) tenga el foco.</summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 0x5100;

    public GlobalHotkeys(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);
    }

    public bool Register(ModifierKeys modifiers, Key key, bool repeat, Action action)
    {
        uint flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            flags |= NativeMethods.MOD_ALT;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            flags |= NativeMethods.MOD_CONTROL;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            flags |= NativeMethods.MOD_SHIFT;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            flags |= NativeMethods.MOD_WIN;
        }

        if (!repeat)
        {
            flags |= NativeMethods.MOD_NOREPEAT;
        }

        var id = _nextId++;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!NativeMethods.RegisterHotKey(_hwnd, id, flags, virtualKey))
        {
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }

        _actions.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
