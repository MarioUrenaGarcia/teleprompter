using System.Windows.Media;

namespace Teleprompter.Models;

/// <summary>Conversion entre colores y su representacion hexadecimal #RRGGBB.</summary>
public static class ColorText
{
    public static bool TryParse(string? text, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (!value.StartsWith('#') && value.Length is 6 or 3)
        {
            value = "#" + value;
        }

        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }

    public static string Normalize(string? text, string fallback)
    {
        return TryParse(text, out var color) ? ToHex(color) : fallback;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color Parse(string? text, Color fallback) => TryParse(text, out var color) ? color : fallback;

    public static SolidColorBrush Brush(string? text, double opacity = 1)
    {
        var color = Parse(text, Colors.White);
        color.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
