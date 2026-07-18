using System.Text;

namespace BuildingBlocks.Web.Csv;

/// <summary>Export CSV mínimo (RFC 4180) compartido por los reportes admin de PaymentApp y
/// PaymentClient (§J.3 del diseño) — sin dependencia externa, solo header + filas.</summary>
public static class CsvWriter
{
    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(Escape)));

        foreach (var row in rows)
            builder.AppendLine(string.Join(',', row.Select(Escape)));

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
