namespace TaxVision.Tasks.ClientForms.Application.Reporting;

/// <summary>
/// Blinda cada celda del CSV contra inyección de fórmulas: un valor que empieza con <c>= + - @</c>
/// (o tab/CR) se prefija con comilla simple, para que Excel/Sheets no lo ejecute al abrir el archivo.
/// El quoting RFC 4180 y el BOM los pone el <c>CsvWriter</c> en el controller (capa web).
/// </summary>
public static class FormReportCsv
{
    public static string Guard(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var first = value[0];
        return first is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    public static IReadOnlyList<string> GuardRow(IEnumerable<string?> row) => row.Select(Guard).ToArray();
}
