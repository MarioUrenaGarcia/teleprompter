namespace Teleprompter.Import;

public sealed record ImportResult(string Markdown, string? Notice);

/// <summary>Error con un mensaje pensado para mostrarse tal cual al usuario.</summary>
public sealed class ImportException(string message, Exception? inner = null) : Exception(message, inner);

public static class DocumentImporter
{
    public const string FileDialogFilter =
        "Documentos compatibles (*.pdf, *.docx, *.md, *.txt)|*.pdf;*.docx;*.md;*.markdown;*.txt" +
        "|PDF (*.pdf)|*.pdf|Word (*.docx)|*.docx|Markdown (*.md)|*.md;*.markdown|Texto (*.txt)|*.txt";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".md", ".markdown", ".txt",
    };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    public static async Task<ImportResult> ImportAsync(string path, IProgress<string>? progress, CancellationToken cancellation)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".doc")
        {
            throw new ImportException("Los archivos .doc antiguos no son compatibles. Ábrelo en Word y guárdalo como .docx.");
        }

        if (!SupportedExtensions.Contains(extension))
        {
            throw new ImportException("Formato no compatible. Usa archivos PDF, DOCX, MD o TXT.");
        }

        var bytes = await Task.Run(() => ReadShared(path), cancellation);
        return extension switch
        {
            ".pdf" => await Task.Run(() => PdfImporter.ImportAsync(bytes, progress, cancellation), cancellation),
            ".docx" => await Task.Run(() => DocxImporter.Import(bytes), cancellation),
            ".txt" => new ImportResult(TextImporter.ImportPlainText(bytes), null),
            _ => new ImportResult(TextImporter.ImportMarkdown(bytes), null),
        };
    }

    /// <summary>
    /// Lee el archivo completo permitiendo que otro programa lo tenga abierto, como Word o un visor de PDF.
    /// </summary>
    private static byte[] ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (FileNotFoundException ex)
        {
            throw new ImportException("El archivo ya no existe.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new ImportException("La carpeta del archivo ya no existe.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ImportException("No hay permiso para leer el archivo.", ex);
        }
        catch (IOException ex)
        {
            throw new ImportException("No se pudo leer el archivo: " + ex.Message, ex);
        }
    }
}
