using System.Globalization;
using System.Windows.Data;

namespace Teleprompter.Controls;

/// <summary>Muestra fracciones (0 a 1) como porcentajes enteros (0 a 100) en los controles deslizantes.</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is double fraction ? Math.Round(fraction * 100) : 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is double percent ? percent / 100.0 : 0d;
    }
}
