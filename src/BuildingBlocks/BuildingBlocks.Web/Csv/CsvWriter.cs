using System.Text;

namespace BuildingBlocks.Web.Csv;

/// <summary>Export CSV mínimo (RFC 4180) compartido por los reportes admin de PaymentApp y
/// PaymentClient (§J.3 del diseño) — sin dependencia externa, solo header + filas.</summary>
public static class CsvWriter
{
    /// <summary>Fin de línea de RFC 4180. Explícito porque los contenedores corren Linux y
    /// <c>AppendLine</c> habría escrito solo LF (BB-17).</summary>
    private const string Crlf = "\r\n";

    /// <summary>
    /// UTF-8 **con** BOM. Sin él, Excel interpreta el archivo con la codepage ANSI del sistema y los
    /// acentos y la ñ de los nombres de clientes salen como mojibake (BB-17). Los reportes admin se
    /// abren en Excel, así que el BOM es parte del contrato, no un detalle.
    /// </summary>
    public static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        WriteTo(builder, headers, rows);
        return builder.ToString();
    }

    /// <summary>
    /// Escribe el CSV completo listo para servir: BOM UTF-8 + contenido. Esta es la forma correcta
    /// de devolverlo desde un endpoint (<c>File(bytes, "text/csv")</c>).
    /// </summary>
    public static byte[] WriteWithBom(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Write(headers, rows));
        var result = new byte[Utf8Bom.Length + content.Length];

        Utf8Bom.CopyTo(result);
        content.CopyTo(result, Utf8Bom.Length);
        return result;
    }

    /// <summary>
    /// Vuelca fila a fila en el <paramref name="output"/> sin materializar el CSV entero. Para un
    /// reporte grande esto evita tener el string completo y su copia en bytes vivos a la vez.
    /// </summary>
    public static void WriteTo(
        StringBuilder output,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows
    )
    {
        output.Append(string.Join(',', headers.Select(Escape))).Append(Crlf);

        foreach (var row in rows)
            output.Append(string.Join(',', row.Select(Escape))).Append(Crlf);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
