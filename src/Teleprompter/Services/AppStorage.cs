using System.Text;
using System.Text.Json;
using Teleprompter.Models;

namespace Teleprompter.Services;

/// <summary>
/// Guarda ajustes y el ultimo guion en la carpeta de datos local del usuario,
/// separada de la carpeta de instalacion para que una actualizacion no los borre.
/// </summary>
public static class AppStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Teleprompter");

    private static string SettingsPath => Path.Combine(DataDirectory, "config.json");

    private static string ScriptPath => Path.Combine(DataDirectory, "guion.md");

    public static string PendingOpenPath => Path.Combine(DataDirectory, "abrir.txt");

    public static string ErrorLogPath => Path.Combine(DataDirectory, "errores.log");

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogError("No se pudieron leer los ajustes", ex);
        }

        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        WriteAtomically(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static string? LoadScript()
    {
        try
        {
            return File.Exists(ScriptPath) ? File.ReadAllText(ScriptPath, Encoding.UTF8) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogError("No se pudo leer el ultimo guion", ex);
            return null;
        }
    }

    public static void SaveScript(string markdown)
    {
        WriteAtomically(ScriptPath, markdown);
    }

    public static void LogError(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Escribe primero a un temporal para no dejar el archivo a medias si el proceso se interrumpe.</summary>
    private static void WriteAtomically(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogError($"No se pudo guardar {Path.GetFileName(path)}", ex);
        }
    }
}
