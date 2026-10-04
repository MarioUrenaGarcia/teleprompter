using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Teleprompter.Models;

public sealed class AppSettings : INotifyPropertyChanged
{
    public const double MinFontSize = 16;
    public const double MaxFontSize = 160;
    public const double MinSpeed = 1;
    public const double MaxSpeed = 200;

    private string _fontFamily = "Segoe UI";
    private double _fontSize = 44;
    private string _fontWeight = "Normal";
    private double _lineSpacing = 1.4;
    private string _textAlignment = "Left";
    private double _marginPercent = 6;
    private string _textColor = "#FFFFFF";
    private string _backgroundColor = "#000000";
    private double _backgroundOpacity = 0.85;
    private double _speed = 18;
    private int _countdownSeconds = 3;
    private bool _showGuide = true;
    private double _guidePosition = 0.3;
    private string _guideColor = "#FFC83D";
    private bool _mirrorHorizontal;
    private bool _mirrorVertical;
    private bool _alwaysOnTop = true;
    private bool _hideFromCapture = true;
    private bool _showInTaskbar = true;
    private bool _clickThrough;
    private bool _globalHotkeys = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value); }

    public double FontSize { get => _fontSize; set => Set(ref _fontSize, Math.Round(Math.Clamp(value, MinFontSize, MaxFontSize))); }

    /// <summary>Normal, SemiBold o Bold.</summary>
    public string FontWeight { get => _fontWeight; set => Set(ref _fontWeight, value is "SemiBold" or "Bold" ? value : "Normal"); }

    public double LineSpacing { get => _lineSpacing; set => Set(ref _lineSpacing, Math.Round(Math.Clamp(value, 1.0, 2.5), 2)); }

    /// <summary>Left, Center, Right o Justify.</summary>
    public string TextAlignment { get => _textAlignment; set => Set(ref _textAlignment, value is "Center" or "Right" or "Justify" ? value : "Left"); }

    public double MarginPercent { get => _marginPercent; set => Set(ref _marginPercent, Math.Round(Math.Clamp(value, 0, 35))); }

    public string TextColor { get => _textColor; set => Set(ref _textColor, ColorText.Normalize(value, "#FFFFFF")); }

    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, ColorText.Normalize(value, "#000000")); }

    public double BackgroundOpacity { get => _backgroundOpacity; set => Set(ref _backgroundOpacity, Math.Round(Math.Clamp(value, 0, 1), 2)); }

    /// <summary>Velocidad de desplazamiento en lineas por minuto.</summary>
    public double Speed { get => _speed; set => Set(ref _speed, Math.Round(Math.Clamp(value, MinSpeed, MaxSpeed))); }

    public int CountdownSeconds { get => _countdownSeconds; set => Set(ref _countdownSeconds, Math.Clamp(value, 0, 10)); }

    public bool ShowGuide { get => _showGuide; set => Set(ref _showGuide, value); }

    /// <summary>Altura de la linea de lectura como fraccion de la ventana.</summary>
    public double GuidePosition { get => _guidePosition; set => Set(ref _guidePosition, Math.Round(Math.Clamp(value, 0.05, 0.8), 2)); }

    public string GuideColor { get => _guideColor; set => Set(ref _guideColor, ColorText.Normalize(value, "#FFC83D")); }

    public bool MirrorHorizontal { get => _mirrorHorizontal; set => Set(ref _mirrorHorizontal, value); }

    public bool MirrorVertical { get => _mirrorVertical; set => Set(ref _mirrorVertical, value); }

    public bool AlwaysOnTop { get => _alwaysOnTop; set => Set(ref _alwaysOnTop, value); }

    public bool HideFromCapture { get => _hideFromCapture; set => Set(ref _hideFromCapture, value); }

    public bool ShowInTaskbar { get => _showInTaskbar; set => Set(ref _showInTaskbar, value); }

    /// <summary>
    /// No se guarda: si la ventana arrancara ignorando el raton y el usuario no recordara el atajo,
    /// no habria forma visible de recuperarla.
    /// </summary>
    [JsonIgnore]
    public bool ClickThrough { get => _clickThrough; set => Set(ref _clickThrough, value); }

    public bool GlobalHotkeys { get => _globalHotkeys; set => Set(ref _globalHotkeys, value); }

    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = double.NaN;
    public double WindowHeight { get; set; } = double.NaN;

    public string? ScriptTitle { get; set; }

    public void ResetAppearanceAndBehavior()
    {
        var defaults = new AppSettings();
        FontFamily = defaults.FontFamily;
        FontSize = defaults.FontSize;
        FontWeight = defaults.FontWeight;
        LineSpacing = defaults.LineSpacing;
        TextAlignment = defaults.TextAlignment;
        MarginPercent = defaults.MarginPercent;
        TextColor = defaults.TextColor;
        BackgroundColor = defaults.BackgroundColor;
        BackgroundOpacity = defaults.BackgroundOpacity;
        Speed = defaults.Speed;
        CountdownSeconds = defaults.CountdownSeconds;
        ShowGuide = defaults.ShowGuide;
        GuidePosition = defaults.GuidePosition;
        GuideColor = defaults.GuideColor;
        MirrorHorizontal = defaults.MirrorHorizontal;
        MirrorVertical = defaults.MirrorVertical;
        AlwaysOnTop = defaults.AlwaysOnTop;
        HideFromCapture = defaults.HideFromCapture;
        ShowInTaskbar = defaults.ShowInTaskbar;
        ClickThrough = false;
        GlobalHotkeys = defaults.GlobalHotkeys;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
