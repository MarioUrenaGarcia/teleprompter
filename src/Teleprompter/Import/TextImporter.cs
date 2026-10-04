using System.Text;

namespace Teleprompter.Import;

public static class TextImporter
{
    public static string ImportMarkdown(byte[] bytes)
    {
        return MarkdownBuilder.CleanText(Decode(bytes)).Trim() + "\n";
    }

    public static string ImportPlainText(byte[] bytes)
    {
        return MarkdownBuilder.EscapeText(Decode(bytes)) + "\n";
    }

    /// <summary>
    /// Respeta la marca BOM si existe; si no, intenta UTF-8 estricto y recurre a Windows-1252,
    /// la codificacion habitual de los .txt guardados con el Bloc de notas antiguo en espanol.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}
