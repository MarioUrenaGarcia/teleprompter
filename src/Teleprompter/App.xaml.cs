using System.Text;
using System.Windows;
using System.Windows.Threading;
using Teleprompter.Services;

namespace Teleprompter;

public partial class App : Application
{
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Windows-1252 para leer archivos de texto antiguos sin codificacion UTF-8.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var fileArgument = e.Args.FirstOrDefault(File.Exists);

        _instance = new SingleInstance();
        if (!_instance.TryAcquire())
        {
            if (fileArgument is not null)
            {
                TryWritePendingFile(fileArgument);
            }

            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        var settings = AppStorage.LoadSettings();
        CaptureShield.Initialize(settings.HideFromCapture);

        var window = new MainWindow(settings);
        MainWindow = window;
        window.Show();

        _instance.Listen(() => Dispatcher.BeginInvoke(() => window.BringToFront(TakePendingFile())));

        if (fileArgument is not null)
        {
            _ = window.OpenFileAsync(fileArgument);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CaptureShield.Shutdown();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppStorage.LogError("Error no controlado", e.Exception);
        MessageBox.Show(
            "Ocurrió un error inesperado. Los detalles se guardaron en:\n" + AppStorage.ErrorLogPath,
            "Teleprompter",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void TryWritePendingFile(string path)
    {
        try
        {
            Directory.CreateDirectory(AppStorage.DataDirectory);
            File.WriteAllText(AppStorage.PendingOpenPath, Path.GetFullPath(path), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppStorage.LogError("No se pudo pasar el archivo a la instancia abierta", ex);
        }
    }

    private static string? TakePendingFile()
    {
        try
        {
            if (!File.Exists(AppStorage.PendingOpenPath))
            {
                return null;
            }

            var path = File.ReadAllText(AppStorage.PendingOpenPath, Encoding.UTF8).Trim();
            File.Delete(AppStorage.PendingOpenPath);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
