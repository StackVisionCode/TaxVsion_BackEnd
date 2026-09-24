using System.Text;

namespace TaxVision.CloudStorage.Application.Files;

/// <summary>Content-Disposition con fallback ASCII (filename=) + UTF-8 RFC 5987 (filename*) para nombres con acentos.</summary>
internal static class ContentDispositionBuilder
{
    public static string Build(string dispositionType, string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName;
        return $"{dispositionType}; filename=\"{ToAsciiFallback(name)}\"; filename*=UTF-8''{Rfc5987Encode(name)}";
    }

    /// <summary>Nombre ASCII seguro (sin comillas, barras ni control) para usar como filename literal, p.ej. el .zip.</summary>
    public static string SafeAsciiFileName(string name)
    {
        var ascii = ToAsciiFallback(string.IsNullOrWhiteSpace(name) ? "download" : name)
            .Replace('/', '_')
            .Replace('\\', '_')
            .Trim();
        return string.IsNullOrEmpty(ascii) ? "download" : ascii;
    }

    private static string ToAsciiFallback(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(ch is >= ' ' and < '' and not '"' and not '\\' ? ch : '_');
        return sb.ToString();
    }

    private static string Rfc5987Encode(string name)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            var c = (char)b;
            if (
                c
                is (>= 'A' and <= 'Z')
                    or (>= 'a' and <= 'z')
                    or (>= '0' and <= '9')
                    or '!'
                    or '#'
                    or '$'
                    or '&'
                    or '+'
                    or '-'
                    or '.'
                    or '^'
                    or '_'
                    or '`'
                    or '|'
                    or '~'
            )
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
