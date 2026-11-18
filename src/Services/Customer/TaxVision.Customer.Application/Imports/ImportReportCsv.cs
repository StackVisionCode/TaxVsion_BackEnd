namespace TaxVision.Customer.Application.Imports;

/// <summary>Celdas del reporte CSV del import, que se abre en Excel.</summary>
public static class ImportReportCsv
{
    // Excel ejecuta una celda que empieza con estos caracteres (inyección de fórmulas).
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        var text = FormulaStarts.Contains(value[0]) ? "'" + value : value;
        return $"\"{text.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ")}\"";
    }
}
