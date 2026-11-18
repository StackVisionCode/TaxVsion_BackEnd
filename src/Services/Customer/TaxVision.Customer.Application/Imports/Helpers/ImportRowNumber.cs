using System.Globalization;

namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Número de cada fila del archivo. El CSV limpio del CRM trae <c>SourceRow</c> (la fila del archivo original), así
/// resultados, reporte y avisos dicen lo mismo que la revisión. Sin la columna, o si el valor no avanza, se cuenta
/// por línea: el número nunca se repite, porque retomar un import salta las filas ya registradas por número.
/// </summary>
public static class ImportRowNumber
{
    public const string SourceRowColumn = "SourceRow";

    public static int Next(string? sourceRow, int previous) =>
        int.TryParse(sourceRow, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > previous
            ? value
            : previous + 1;
}
