using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Teleprompter.Models;

namespace Teleprompter.Controls;

public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color),
        typeof(string),
        typeof(ColorPicker),
        new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    private static readonly (string Hex, string Name)[] Palette =
    [
        ("#FFFFFF", "Blanco"),
        ("#FFF4C2", "Crema"),
        ("#FFD60A", "Amarillo"),
        ("#FFC83D", "Ámbar"),
        ("#7CFC9A", "Verde"),
        ("#6FD3FF", "Celeste"),
        ("#FF8A80", "Coral"),
        ("#C7B8FF", "Lavanda"),
        ("#808080", "Gris"),
        ("#1E1E1E", "Grafito"),
        ("#0B2545", "Azul marino"),
        ("#000000", "Negro"),
    ];

    private readonly List<Swatch> _swatches;

    public ColorPicker()
    {
        InitializeComponent();
        _swatches = Palette.Select(p => new Swatch(p.Hex, p.Name)).ToList();
        Swatches.ItemsSource = _swatches;
        Refresh();
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ColorPicker)d).Refresh();
    }

    private void Refresh()
    {
        if (_swatches is null)
        {
            return;
        }

        var current = ColorText.Normalize(Color, "#FFFFFF");
        HexBox.Text = current;
        foreach (var swatch in _swatches)
        {
            swatch.IsSelected = string.Equals(swatch.Hex, current, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
        {
            Color = hex;
        }
    }

    private void HexBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitHex();

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
    }

    private void CommitHex()
    {
        if (ColorText.TryParse(HexBox.Text, out var parsed))
        {
            Color = ColorText.ToHex(parsed);
        }

        Refresh();
    }

    public sealed class Swatch(string hex, string name) : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Hex { get; } = hex;

        public string Name { get; } = name;

        public Brush Brush { get; } = ColorText.Brush(hex);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
