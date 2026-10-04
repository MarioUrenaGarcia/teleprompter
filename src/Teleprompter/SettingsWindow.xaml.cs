using System.Windows;
using System.Windows.Media;
using Teleprompter.Models;
using Teleprompter.Services;

namespace Teleprompter;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MainWindow _main;

    public SettingsWindow(AppSettings settings, MainWindow main)
    {
        _settings = settings;
        _main = main;
        InitializeComponent();
        DataContext = settings;

        var fonts = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(name => !name.StartsWith('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (!fonts.Contains(settings.FontFamily, StringComparer.OrdinalIgnoreCase))
        {
            fonts.Insert(0, settings.FontFamily);
        }

        FontCombo.ItemsSource = fonts;
        WeightCombo.ItemsSource = new[]
        {
            new Option("Normal", "Normal"),
            new Option("SemiBold", "Seminegrita"),
            new Option("Bold", "Negrita"),
        };
        AlignmentCombo.ItemsSource = new[]
        {
            new Option("Left", "Izquierda"),
            new Option("Center", "Centrado"),
            new Option("Right", "Derecha"),
            new Option("Justify", "Justificado"),
        };

        _main.StatusChanged += OnMainStatusChanged;
        Closed += (_, _) => _main.StatusChanged -= OnMainStatusChanged;
        Loaded += (_, _) =>
        {
            PlaceNextToOwner();
            RefreshStatus();
        };
    }

    private void OnMainStatusChanged(object? sender, EventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        CaptureStatus.Text = _main.CaptureState switch
        {
            CaptureState.Hidden => "Activo: el teleprompter, sus menús y esta ventana no aparecen al compartir pantalla, en grabaciones ni en capturas.",
            CaptureState.BlackedOut => "Esta versión de Windows no permite excluir la ventana: se verá como un recuadro negro. Se necesita Windows 10 versión 2004 o posterior.",
            _ => "Desactivado: las personas con las que compartes pantalla verán el teleprompter.",
        };

        var failed = _main.FailedHotkeys.Select(h => h.Action).ToHashSet();
        HotkeyList.ItemsSource = HotkeyCatalog.All
            .Select(h => new HotkeyRow(
                h.Display,
                failed.Contains(h.Action) ? h.Description + " (ocupado por otra aplicación)" : h.Description,
                _settings.GlobalHotkeys && !failed.Contains(h.Action) ? 1.0 : 0.5))
            .ToList();

        HotkeyWarning.Visibility = failed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HotkeyWarning.Text = failed.Count > 0
            ? "Algunos atajos ya los usa otra aplicación y no se pudieron activar. Los controles de la ventana siguen funcionando."
            : string.Empty;
    }

    /// <summary>Abre los ajustes junto al teleprompter para poder ver los cambios en vivo sin taparlo.</summary>
    private void PlaceNextToOwner()
    {
        const double gap = 12;
        var work = SystemParameters.WorkArea;
        var owner = new Rect(_main.Left, _main.Top, _main.ActualWidth, _main.ActualHeight);
        var height = Math.Min(Height, work.Height);
        var width = ActualWidth;

        double left;
        double top;
        if (owner.Right + gap + width <= work.Right)
        {
            left = owner.Right + gap;
            top = owner.Top;
        }
        else if (owner.Left - gap - width >= work.Left)
        {
            left = owner.Left - gap - width;
            top = owner.Top;
        }
        else if (work.Bottom - (owner.Bottom + gap) >= MinHeight)
        {
            left = owner.Left + (owner.Width - width) / 2;
            top = owner.Bottom + gap;
            height = Math.Min(height, work.Bottom - top);
        }
        else if (owner.Top - gap - work.Top >= MinHeight)
        {
            height = Math.Min(height, owner.Top - gap - work.Top);
            left = owner.Left + (owner.Width - width) / 2;
            top = owner.Top - gap - height;
        }
        else
        {
            left = work.Left + (work.Width - width) / 2;
            top = work.Top + (work.Height - height) / 2;
        }

        Height = height;
        Left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        Top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetAppearanceAndBehavior();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public sealed record Option(string Value, string Label);

    public sealed record HotkeyRow(string Keys, string Description, double Opacity);
}
