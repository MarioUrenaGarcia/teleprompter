using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Markdig.Syntax;
using Microsoft.Win32;
using Teleprompter.Import;
using Teleprompter.Models;
using Teleprompter.Rendering;
using Teleprompter.Services;

namespace Teleprompter;

public partial class MainWindow : Window
{
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";
    private const string LockGlyph = "\uE72E";
    private const string UnlockGlyph = "\uE785";
    private const string FullScreenGlyph = "\uE740";
    private const string BackToWindowGlyph = "\uE73F";

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _renderThrottle;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _toolbarHideTimer;

    private IntPtr _hwnd;
    private GlobalHotkeys? _hotkeys;
    private SettingsWindow? _settingsWindow;
    private CancellationTokenSource? _importCancellation;

    private string _markdown = string.Empty;
    private string? _title;
    private MarkdownDocument? _document;

    private double _offset;
    private double _targetOffset;
    private double _contentHeight;
    private bool _playing;
    private int _countdownRemaining;
    private bool _renderingHooked;
    private TimeSpan _lastFrameTime;
    private TimeSpan _lastStatusTime;

    private bool _editing;
    private string _markdownBeforeEdit = string.Empty;
    private bool _loading;
    private bool _draggingPosition;
    private bool _toolbarVisible = true;
    private bool _fullScreen;
    private Rect _windowedBounds;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _renderThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _renderThrottle.Tick += (_, _) =>
        {
            _renderThrottle.Stop();
            RenderScript();
        };
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        };
        _toolbarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _toolbarHideTimer.Tick += (_, _) =>
        {
            _toolbarHideTimer.Stop();
            UpdateToolbarVisibility();
        };

        RestoreWindowBounds();
        Topmost = _settings.AlwaysOnTop;
        ShowInTaskbar = _settings.ShowInTaskbar;
        MinimizeButton.Visibility = _settings.ShowInTaskbar ? Visibility.Visible : Visibility.Collapsed;
        ApplyBackground();
        ApplyMirror();
        UpdateSpeedText();

        _settings.PropertyChanged += OnSettingChanged;
        ScriptPanel.SizeChanged += OnScriptSizeChanged;
        Viewport.SizeChanged += (_, _) => UpdateLayoutMetrics();
        MouseEnter += (_, _) =>
        {
            _toolbarHideTimer.Stop();
            UpdateToolbarVisibility();
        };
        MouseLeave += (_, _) => _toolbarHideTimer.Start();

        var saved = AppStorage.LoadScript();
        if (!string.IsNullOrWhiteSpace(saved))
        {
            LoadScript(saved, _settings.ScriptTitle, persist: false);
        }
        else
        {
            UpdateEmptyState();
            UpdateStatus();
        }
    }

    /// <summary>Se dispara cuando cambia el estado de proteccion o de los atajos, para refrescar la ventana de ajustes.</summary>
    public event EventHandler? StatusChanged;

    public IReadOnlyList<HotkeyDefinition> FailedHotkeys { get; private set; } = [];

    public CaptureState CaptureState => CaptureShield.GetState(this);

    private double LineHeight => _settings.FontSize * _settings.LineSpacing;

    private double PixelsPerSecond => _settings.Speed * LineHeight / 60.0;

    private double MaxOffset => Math.Max(0, _contentHeight - LineHeight);

    private double GuideY => Math.Round(Viewport.ActualHeight * _settings.GuidePosition);

    private bool HasScript => _document is not null && ScriptRenderer.HasContent(_document);

    private bool CountdownActive => _countdownTimer.IsEnabled;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        CaptureShield.Apply(this);
        WindowStyles.SetClickThrough(_hwnd, _settings.ClickThrough);
        RegisterHotkeys();
        UpdateShieldButton();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        _importCancellation?.Cancel();
        if (_editing)
        {
            FinishEditing(commit: true);
        }

        StopPlayback();
        // Se guarda siempre el tamano de ventana normal: la aplicacion vuelve a abrir en modo ventana.
        var bounds = _fullScreen
            ? _windowedBounds
            : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        AppStorage.SaveSettings(_settings);
        AppStorage.SaveScript(_markdown);
        _settingsWindow?.Close();
        _hotkeys?.Dispose();
        _hotkeys = null;
    }

    // Guion

    public void LoadScript(string markdown, string? title, bool persist = true, bool resetPosition = true)
    {
        _markdown = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        _title = string.IsNullOrWhiteSpace(title) ? null : title;
        _settings.ScriptTitle = _title;
        _document = ScriptRenderer.Parse(_markdown);

        if (resetPosition)
        {
            _offset = 0;
            _targetOffset = 0;
        }

        RenderScript();
        UpdateEmptyState();
        UpdateStatus();
        if (persist)
        {
            AppStorage.SaveScript(_markdown);
        }
    }

    private void RenderScript()
    {
        if (_document is null || !HasScript)
        {
            ScriptPanel.Children.Clear();
            return;
        }

        var fontFamily = new FontFamily(_settings.FontFamily);
        var weight = _settings.FontWeight switch
        {
            "Bold" => FontWeights.Bold,
            "SemiBold" => FontWeights.SemiBold,
            _ => FontWeights.Normal,
        };
        var alignment = _settings.TextAlignment switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            "Justify" => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };

        var options = new ScriptStyle(
            fontFamily,
            _settings.FontSize,
            weight,
            _settings.LineSpacing,
            alignment,
            ColorText.Brush(_settings.TextColor),
            ColorText.Brush(_settings.GuideColor));

        ScriptRenderer.Render(_document, ScriptPanel, options);
    }

    private void ScheduleRender()
    {
        if (!_renderThrottle.IsEnabled)
        {
            _renderThrottle.Start();
        }
    }

    /// <summary>
    /// Cuando cambia la altura del texto (otra letra, otro ancho de ventana) se conserva la posicion
    /// relativa para no perder el punto de lectura.
    /// </summary>
    private void OnScriptSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var previous = e.PreviousSize.Height;
        var current = e.NewSize.Height;
        _contentHeight = current;
        if (previous > 0 && current > 0 && Math.Abs(previous - current) > 0.5)
        {
            var ratio = current / previous;
            _offset *= ratio;
            _targetOffset *= ratio;
        }

        _offset = Math.Clamp(_offset, 0, MaxOffset);
        _targetOffset = Math.Clamp(_targetOffset, 0, MaxOffset);
        ApplyOffset();
        UpdateGuide();
        UpdateStatus();
    }

    // Desplazamiento

    private void ApplyOffset()
    {
        ScrollTransform.Y = GuideY - _offset;
        UpdatePositionBar();
    }

    private void ScrollBy(double pixels)
    {
        if (!HasScript || _editing)
        {
            return;
        }

        _targetOffset = Math.Clamp(_targetOffset + pixels, 0, MaxOffset);
        EnsureRendering();
    }

    private void JumpTo(double offset, bool instant = false)
    {
        if (!HasScript)
        {
            return;
        }

        _targetOffset = Math.Clamp(offset, 0, MaxOffset);
        if (instant)
        {
            _offset = _targetOffset;
            ApplyOffset();
            UpdateStatus();
        }

        EnsureRendering();
    }

    private void EnsureRendering()
    {
        if (_renderingHooked)
        {
            return;
        }

        _renderingHooked = true;
        _lastFrameTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_renderingHooked)
        {
            return;
        }

        _renderingHooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == _lastFrameTime)
        {
            return;
        }

        // Tras una pausa del sistema el intervalo puede ser enorme; se limita para no dar saltos.
        var delta = _lastFrameTime == TimeSpan.Zero ? 0 : Math.Min((time - _lastFrameTime).TotalSeconds, 0.1);
        _lastFrameTime = time;

        if (_playing)
        {
            _targetOffset += PixelsPerSecond * delta;
            if (_targetOffset >= MaxOffset)
            {
                _targetOffset = MaxOffset;
                _playing = false;
                UpdatePlayState();
                ShowToast("Fin del guion");
            }
        }

        // Aproximacion exponencial: los saltos manuales se deslizan en lugar de cortar la lectura.
        var difference = _targetOffset - _offset;
        if (Math.Abs(difference) < 0.05)
        {
            _offset = _targetOffset;
        }
        else
        {
            _offset += difference * (1 - Math.Exp(-delta * 14));
        }

        ApplyOffset();

        if (time - _lastStatusTime > TimeSpan.FromMilliseconds(250))
        {
            _lastStatusTime = time;
            UpdateStatus();
        }

        if (!_playing && _offset == _targetOffset)
        {
            UpdateStatus();
            StopRendering();
        }
    }

    private void TogglePlay()
    {
        if (_editing || _loading || !HasScript)
        {
            return;
        }

        if (_playing || CountdownActive)
        {
            StopPlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void StartPlayback()
    {
        if (_targetOffset >= MaxOffset - 1)
        {
            JumpTo(0, instant: true);
        }

        if (_settings.CountdownSeconds > 0)
        {
            _countdownRemaining = _settings.CountdownSeconds;
            CountdownText.Text = _countdownRemaining.ToString();
            CountdownBadge.Visibility = Visibility.Visible;
            _countdownTimer.Start();
            UpdatePlayState();
            return;
        }

        BeginScrolling();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _countdownRemaining--;
        if (_countdownRemaining > 0)
        {
            CountdownText.Text = _countdownRemaining.ToString();
            return;
        }

        _countdownTimer.Stop();
        CountdownBadge.Visibility = Visibility.Collapsed;
        BeginScrolling();
    }

    private void BeginScrolling()
    {
        _playing = true;
        EnsureRendering();
        UpdatePlayState();
    }

    private void StopPlayback()
    {
        _countdownTimer.Stop();
        CountdownBadge.Visibility = Visibility.Collapsed;
        _playing = false;
        UpdatePlayState();
    }

    private void UpdatePlayState()
    {
        var active = _playing || CountdownActive;
        PlayButton.Content = active ? PauseGlyph : PlayGlyph;
        UpdateToolbarVisibility();
        UpdateStatus();
    }

    private void ChangeSpeed(double delta)
    {
        _settings.Speed += delta;
        ShowToast($"Velocidad: {_settings.Speed:0} líneas por minuto", brief: true);
    }

    private void ChangeFontSize(double delta)
    {
        _settings.FontSize += delta;
    }

    // Apariencia

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.FontFamily):
            case nameof(AppSettings.FontWeight):
            case nameof(AppSettings.TextAlignment):
            case nameof(AppSettings.TextColor):
                ScheduleRender();
                break;
            case nameof(AppSettings.FontSize):
            case nameof(AppSettings.LineSpacing):
                ScheduleRender();
                UpdateGuide();
                UpdateStatus();
                break;
            case nameof(AppSettings.MarginPercent):
                UpdateLayoutMetrics();
                break;
            case nameof(AppSettings.BackgroundColor):
            case nameof(AppSettings.BackgroundOpacity):
                ApplyBackground();
                break;
            case nameof(AppSettings.Speed):
                UpdateSpeedText();
                UpdateStatus();
                break;
            case nameof(AppSettings.ShowGuide):
            case nameof(AppSettings.GuidePosition):
            case nameof(AppSettings.GuideColor):
                UpdateGuide();
                ApplyOffset();
                if (e.PropertyName == nameof(AppSettings.GuideColor))
                {
                    ScheduleRender();
                }

                break;
            case nameof(AppSettings.MirrorHorizontal):
            case nameof(AppSettings.MirrorVertical):
                ApplyMirror();
                break;
            case nameof(AppSettings.AlwaysOnTop):
                Topmost = _settings.AlwaysOnTop;
                break;
            case nameof(AppSettings.HideFromCapture):
                CaptureShield.Enabled = _settings.HideFromCapture;
                UpdateShieldButton();
                ShowToast(_settings.HideFromCapture
                    ? "Invisible al compartir pantalla"
                    : "Atención: ahora la ventana SÍ se ve al compartir pantalla");
                break;
            case nameof(AppSettings.ShowInTaskbar):
                ShowInTaskbar = _settings.ShowInTaskbar;
                MinimizeButton.Visibility = _settings.ShowInTaskbar ? Visibility.Visible : Visibility.Collapsed;
                CaptureShield.ApplyToAllWindows();
                WindowStyles.SetClickThrough(_hwnd, _settings.ClickThrough);
                break;
            case nameof(AppSettings.ClickThrough):
                WindowStyles.SetClickThrough(_hwnd, _settings.ClickThrough);
                UpdateToolbarVisibility();
                ShowToast(_settings.ClickThrough
                    ? "Los clics ahora pasan a través de la ventana. Ctrl + Alt + T para volver."
                    : "La ventana vuelve a responder al ratón.");
                break;
            case nameof(AppSettings.GlobalHotkeys):
                RegisterHotkeys();
                break;
        }
    }

    private void ApplyBackground()
    {
        // Con opacidad cero la ventana en capas dejaria de recibir clics en las zonas vacias.
        RootBorder.Background = ColorText.Brush(_settings.BackgroundColor, Math.Max(_settings.BackgroundOpacity, 0.004));
    }

    private void ApplyMirror()
    {
        MirrorTransform.ScaleX = _settings.MirrorHorizontal ? -1 : 1;
        MirrorTransform.ScaleY = _settings.MirrorVertical ? -1 : 1;
    }

    private void UpdateLayoutMetrics()
    {
        var width = Viewport.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var margin = 22 + width * _settings.MarginPercent / 100.0;
        Canvas.SetLeft(ScriptPanel, margin);
        ScriptPanel.Width = Math.Max(60, width - margin * 2);
        UpdateGuide();
        ApplyOffset();
    }

    private void UpdateGuide()
    {
        GuideBand.Visibility = _settings.ShowGuide && HasScript ? Visibility.Visible : Visibility.Collapsed;
        GuideBand.Margin = new Thickness(0, GuideY, 0, 0);
        GuideBand.Height = LineHeight;
        GuideFill.Background = ColorText.Brush(_settings.GuideColor, 0.15);
        var arrow = ColorText.Brush(_settings.GuideColor);
        GuideArrowLeft.Fill = arrow;
        GuideArrowRight.Fill = arrow;
    }

    private void UpdateSpeedText()
    {
        SpeedText.Text = $"{_settings.Speed:0} lín/min";
    }

    private void UpdatePositionBar()
    {
        if (!HasScript || _editing || MaxOffset <= 0)
        {
            PositionBar.Visibility = Visibility.Collapsed;
            return;
        }

        PositionBar.Visibility = Visibility.Visible;
        var track = PositionBar.ActualHeight;
        if (track <= 0)
        {
            return;
        }

        var visibleRatio = Math.Clamp(Viewport.ActualHeight / (_contentHeight + Viewport.ActualHeight), 0.05, 1);
        var thumb = Math.Max(24, track * visibleRatio);
        PositionThumb.Height = thumb;
        PositionThumb.Margin = new Thickness(0, (track - thumb) * (_offset / MaxOffset), 0, 0);
    }

    private void UpdateStatus()
    {
        if (!HasScript)
        {
            StatusText.Text = string.Empty;
            return;
        }

        var progress = MaxOffset > 0 ? _offset / MaxOffset : 1;
        var remaining = PixelsPerSecond > 0 ? TimeSpan.FromSeconds((MaxOffset - _offset) / PixelsPerSecond) : TimeSpan.Zero;
        var time = remaining.TotalHours >= 1 ? remaining.ToString(@"h\:mm\:ss") : remaining.ToString(@"m\:ss");
        var title = _title ?? "Guion";
        StatusText.Text = $"{title}  ·  {progress:P0}  ·  quedan {time}";
    }

    private void UpdateEmptyState()
    {
        var empty = !HasScript && !_editing;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        UpdateGuide();
        UpdatePositionBar();
    }

    private void UpdateToolbarVisibility()
    {
        var show = !_settings.ClickThrough
            && (_editing || _loading || IsMouseOver || (!_playing && !CountdownActive));

        if (show == _toolbarVisible)
        {
            return;
        }

        _toolbarVisible = show;
        Toolbar.IsHitTestVisible = show;
        Toolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 150 : 350)));
    }

    private void UpdateShieldButton()
    {
        var state = _hwnd == IntPtr.Zero ? CaptureState.Visible : CaptureShield.GetState(this);
        switch (state)
        {
            case CaptureState.Hidden:
                ShieldButton.Content = LockGlyph;
                ShieldButton.Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0xE7, 0x9A));
                ShieldButton.ToolTip = "Invisible al compartir pantalla. Clic para desactivar.";
                break;
            case CaptureState.BlackedOut:
                ShieldButton.Content = LockGlyph;
                ShieldButton.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D));
                ShieldButton.ToolTip = "Esta versión de Windows muestra la ventana como un recuadro negro al compartir pantalla.";
                break;
            default:
                ShieldButton.Content = UnlockGlyph;
                ShieldButton.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));
                ShieldButton.ToolTip = "Visible al compartir pantalla. Clic para ocultarla.";
                break;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowToast(string message, bool brief = false)
    {
        ToastText.Text = message;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(brief ? 1.4 : Math.Clamp(message.Length / 18.0, 2.5, 7));
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        _toastTimer.Start();
    }

    // Edicion

    private void BeginEditing()
    {
        if (_loading || _editing)
        {
            return;
        }

        StopPlayback();
        _editing = true;
        _markdownBeforeEdit = _markdown;
        Editor.Text = _markdown;
        Editor.FontFamily = new FontFamily(_settings.FontFamily);
        Editor.Foreground = ColorText.Brush(_settings.TextColor);
        Editor.CaretBrush = Editor.Foreground;

        MirrorHost.Visibility = Visibility.Hidden;
        Editor.Visibility = Visibility.Visible;
        EditHint.Visibility = Visibility.Visible;
        MainGroup.Visibility = Visibility.Collapsed;
        EditGroup.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
        UpdateEmptyState();
        UpdateToolbarVisibility();

        Editor.Focus();
        Editor.CaretIndex = 0;
        Editor.ScrollToHome();
    }

    private void FinishEditing(bool commit)
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        var text = commit ? Editor.Text : _markdownBeforeEdit;

        Editor.Visibility = Visibility.Collapsed;
        EditHint.Visibility = Visibility.Collapsed;
        MirrorHost.Visibility = Visibility.Visible;
        MainGroup.Visibility = Visibility.Visible;
        EditGroup.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Visible;
        Editor.Text = string.Empty;

        if (commit && text != _markdownBeforeEdit)
        {
            var hadScript = HasScript;
            LoadScript(text, _title ?? "Guion", persist: true, resetPosition: !hadScript);
        }
        else
        {
            UpdateEmptyState();
        }

        UpdateToolbarVisibility();
        Focus();
    }

    // Archivos

    private async Task PromptOpenAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Abrir guion",
            Filter = DocumentImporter.FileDialogFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await OpenFileAsync(dialog.FileName);
        }
    }

    public async Task OpenFileAsync(string path)
    {
        if (_editing)
        {
            FinishEditing(commit: true);
        }

        StopPlayback();
        _importCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;

        SetLoading(true, Path.GetFileName(path));
        try
        {
            var progress = new Progress<string>(message => LoadingDetail.Text = message);
            var result = await DocumentImporter.ImportAsync(path, progress, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            LoadScript(result.Markdown, Path.GetFileNameWithoutExtension(path));
            ShowToast(result.Notice ?? $"Guion cargado: {Path.GetFileName(path)}");
        }
        catch (OperationCanceledException)
        {
            ShowToast("Apertura cancelada");
        }
        catch (ImportException ex)
        {
            AppStorage.LogError($"Importando {Path.GetFileName(path)}", ex);
            ShowToast(ex.Message);
        }
        catch (Exception ex)
        {
            AppStorage.LogError($"Importando {Path.GetFileName(path)}", ex);
            ShowToast("No se pudo abrir el archivo: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_importCancellation, cancellation))
            {
                _importCancellation = null;
                SetLoading(false, null);
            }

            cancellation.Dispose();
        }
    }

    private void SetLoading(bool loading, string? fileName)
    {
        _loading = loading;
        LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        LoadingTitle.Text = loading ? $"Abriendo {fileName}" : string.Empty;
        LoadingDetail.Text = loading ? "Preparando…" : string.Empty;
        if (loading)
        {
            EmptyState.Visibility = Visibility.Collapsed;
        }
        else
        {
            UpdateEmptyState();
        }

        UpdateToolbarVisibility();
    }

    private void SaveScriptAs()
    {
        if (!HasScript)
        {
            ShowToast("Todavía no hay un guion para guardar.");
            return;
        }

        var baseName = string.Concat((_title ?? "Guion").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog
        {
            Title = "Guardar guion",
            Filter = "Markdown (*.md)|*.md|Texto (*.txt)|*.txt",
            FileName = baseName,
            AddExtension = true,
            DefaultExt = ".md",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var asText = string.Equals(Path.GetExtension(dialog.FileName), ".txt", StringComparison.OrdinalIgnoreCase);
            var content = asText ? ScriptRenderer.ToPlainText(_markdown) : _markdown;
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
            _title = Path.GetFileNameWithoutExtension(dialog.FileName);
            _settings.ScriptTitle = _title;
            UpdateStatus();
            ShowToast("Guion guardado");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("No se pudo guardar: " + ex.Message);
        }
    }

    // Ventana

    public void BringToFront(string? pendingPath)
    {
        // Volver a abrir la aplicacion es la salida de emergencia si la ventana quedo ignorando el raton.
        _settings.ClickThrough = false;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = _settings.AlwaysOnTop;

        if (!string.IsNullOrEmpty(pendingPath))
        {
            _ = OpenFileAsync(pendingPath);
        }
    }

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, this) { Owner = this };
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            AppStorage.SaveSettings(_settings);
        };
        _settingsWindow.Show();
    }

    private void RestoreWindowBounds()
    {
        var work = SystemParameters.WorkArea;
        var width = double.IsFinite(_settings.WindowWidth) ? Math.Max(MinWidth, _settings.WindowWidth) : Math.Min(900, work.Width * 0.7);
        var height = double.IsFinite(_settings.WindowHeight) ? Math.Max(MinHeight, _settings.WindowHeight) : Math.Min(340, work.Height * 0.4);
        var left = double.IsFinite(_settings.WindowLeft) ? _settings.WindowLeft : work.Left + (work.Width - width) / 2;
        var top = double.IsFinite(_settings.WindowTop) ? _settings.WindowTop : work.Top + 24;

        // Si el monitor donde estaba la ventana ya no existe, se vuelve a la posicion por defecto.
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screen, new Rect(left, top, width, height));
        if (visible.IsEmpty || visible.Width < 120 || visible.Height < 60)
        {
            width = Math.Min(900, work.Width * 0.7);
            height = Math.Min(340, work.Height * 0.4);
            left = work.Left + (work.Width - width) / 2;
            top = work.Top + 24;
        }

        Width = width;
        Height = height;
        Left = left;
        Top = top;
    }

    private void RegisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _hotkeys ??= new GlobalHotkeys(this);
        _hotkeys.UnregisterAll();

        var failed = new List<HotkeyDefinition>();
        if (_settings.GlobalHotkeys)
        {
            foreach (var definition in HotkeyCatalog.All)
            {
                var action = definition.Action;
                if (!_hotkeys.Register(definition.Modifiers, definition.Key, definition.Repeat, () => ExecuteHotkey(action)))
                {
                    failed.Add(definition);
                }
            }
        }

        FailedHotkeys = failed;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.TogglePlay:
                TogglePlay();
                break;
            case HotkeyAction.Faster:
                ChangeSpeed(1);
                break;
            case HotkeyAction.Slower:
                ChangeSpeed(-1);
                break;
            case HotkeyAction.ScrollBack:
                ScrollBy(-LineHeight * 3);
                break;
            case HotkeyAction.ScrollForward:
                ScrollBy(LineHeight * 3);
                break;
            case HotkeyAction.Restart:
                JumpTo(0);
                break;
            case HotkeyAction.ToggleVisibility:
                if (IsVisible)
                {
                    StopPlayback();
                    Hide();
                }
                else
                {
                    // Mostrar sin activar: el foco se queda en la aplicacion que se esta usando.
                    ShowActivated = false;
                    Show();
                    ShowActivated = true;
                }

                break;
            case HotkeyAction.ToggleClickThrough:
                _settings.ClickThrough = !_settings.ClickThrough;
                break;
        }
    }

    // Teclado y raton

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (_editing)
        {
            if (e.Key == Key.Escape)
            {
                FinishEditing(commit: true);
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.S)
            {
                FinishEditing(commit: true);
                SaveScriptAs();
                e.Handled = true;
            }

            return;
        }

        if (_loading)
        {
            if (e.Key == Key.Escape)
            {
                _importCancellation?.Cancel();
                e.Handled = true;
            }

            return;
        }

        var handled = true;
        switch (e.Key)
        {
            case Key.Space:
                TogglePlay();
                break;
            case Key.Escape:
                if (_playing || CountdownActive || !_fullScreen)
                {
                    StopPlayback();
                }
                else
                {
                    ExitFullScreen();
                }

                break;
            case Key.Up:
                ScrollBy(-LineHeight);
                break;
            case Key.Down:
                ScrollBy(LineHeight);
                break;
            case Key.PageUp:
                ScrollBy(-Viewport.ActualHeight * 0.75);
                break;
            case Key.PageDown:
                ScrollBy(Viewport.ActualHeight * 0.75);
                break;
            case Key.Home:
                JumpTo(0);
                break;
            case Key.End:
                JumpTo(MaxOffset);
                break;
            case Key.Left or Key.Subtract or Key.OemMinus:
                if (ctrl)
                {
                    ChangeFontSize(-2);
                }
                else
                {
                    ChangeSpeed(-1);
                }

                break;
            case Key.Right or Key.Add or Key.OemPlus:
                if (ctrl)
                {
                    ChangeFontSize(2);
                }
                else
                {
                    ChangeSpeed(1);
                }

                break;
            case Key.O when ctrl:
                _ = PromptOpenAsync();
                break;
            case Key.E when ctrl:
                BeginEditing();
                break;
            case Key.S when ctrl:
                SaveScriptAs();
                break;
            case Key.OemComma when ctrl:
                OpenSettings();
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (_editing || _loading)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ChangeFontSize(e.Delta > 0 ? 2 : -2);
        }
        else
        {
            ScrollBy(-e.Delta / 120.0 * LineHeight);
        }

        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = TryGetDroppedFile(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override async void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (TryGetDroppedFile(e, out var path))
        {
            e.Handled = true;
            Activate();
            await OpenFileAsync(path);
        }
        else
        {
            ShowToast("Suelta un archivo PDF, DOCX, MD o TXT.");
        }
    }

    private static bool TryGetDroppedFile(DragEventArgs e, out string path)
    {
        path = string.Empty;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && DocumentImporter.IsSupported(files[0]))
        {
            path = files[0];
            return true;
        }

        return false;
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing || e.OriginalSource is Button)
        {
            return;
        }

        HandleWindowMouseDown(e);
    }

    private void Toolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HandleWindowMouseDown(e);
    }

    /// <summary>Arrastrar mueve la ventana; doble clic alterna entre pantalla completa y modo ventana.</summary>
    private void HandleWindowMouseDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        TryDragMove();
    }

    private void TryDragMove()
    {
        if (_fullScreen)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void PositionBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingPosition = true;
        PositionBar.CaptureMouse();
        SeekToPointer(e);
        e.Handled = true;
    }

    private void PositionBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingPosition)
        {
            SeekToPointer(e);
        }
    }

    private void PositionBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingPosition = false;
        PositionBar.ReleaseMouseCapture();
    }

    private void SeekToPointer(MouseEventArgs e)
    {
        var track = PositionBar.ActualHeight;
        if (track <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(e.GetPosition(PositionBar).Y / track, 0, 1);
        JumpTo(ratio * MaxOffset, instant: true);
    }

    // Botones

    private async void Open_Click(object sender, RoutedEventArgs e) => await PromptOpenAsync();

    private void Edit_Click(object sender, RoutedEventArgs e) => BeginEditing();

    private void Save_Click(object sender, RoutedEventArgs e) => SaveScriptAs();

    private void FinishEdit_Click(object sender, RoutedEventArgs e) => FinishEditing(commit: true);

    private void CancelEdit_Click(object sender, RoutedEventArgs e) => FinishEditing(commit: false);

    private void Restart_Click(object sender, RoutedEventArgs e) => JumpTo(0);

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlay();

    private void Slower_Click(object sender, RoutedEventArgs e) => ChangeSpeed(-1);

    private void Faster_Click(object sender, RoutedEventArgs e) => ChangeSpeed(1);

    private void SmallerFont_Click(object sender, RoutedEventArgs e) => ChangeFontSize(-2);

    private void LargerFont_Click(object sender, RoutedEventArgs e) => ChangeFontSize(2);

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void Shield_Click(object sender, RoutedEventArgs e) => _settings.HideFromCapture = !_settings.HideFromCapture;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    // Pantalla completa

    private void ToggleFullScreen()
    {
        if (_fullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        if (_fullScreen || _hwnd == IntPtr.Zero)
        {
            return;
        }

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        _windowedBounds = new Rect(Left, Top, Width, Height);
        _fullScreen = true;
        ApplyWindowMode();
        if (!WindowStyles.CoverMonitor(_hwnd))
        {
            _fullScreen = false;
            ApplyWindowMode();
        }
    }

    private void ExitFullScreen()
    {
        if (!_fullScreen)
        {
            return;
        }

        _fullScreen = false;
        ApplyWindowMode();
        Width = _windowedBounds.Width;
        Height = _windowedBounds.Height;
        Left = _windowedBounds.Left;
        Top = _windowedBounds.Top;
    }

    /// <summary>En pantalla completa no hay esquinas redondeadas ni bordes para redimensionar.</summary>
    private void ApplyWindowMode()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(_fullScreen ? 0 : 7),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var radius = _fullScreen ? 0 : 12;
        RootBorder.CornerRadius = new CornerRadius(radius);
        RootBorder.BorderThickness = new Thickness(_fullScreen ? 0 : 1);
        LoadingOverlay.CornerRadius = new CornerRadius(radius);
        Toolbar.CornerRadius = _fullScreen ? new CornerRadius(0) : new CornerRadius(11, 11, 0, 0);

        FullScreenButton.Content = _fullScreen ? BackToWindowGlyph : FullScreenGlyph;
        FullScreenButton.ToolTip = _fullScreen ? "Modo ventana (F11 o Esc)" : "Pantalla completa (F11)";
    }
}
