using System.Globalization;
using System.Text.Json;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

/// <summary>Un valor normalizado listo para indexar: siempre texto, y además número o fecha tipados.</summary>
public readonly record struct FormReportCell(string Text, decimal? Number, DateOnly? Date);

/// <summary>
/// Normaliza el valor de un componente <c>reportable</c> al formato del índice (P-12). Un componente
/// puede rendir varios valores (checkboxGroup). Nunca indexa <c>taxId</c> ni <c>file</c>.
/// </summary>
public static class FormReportValueNormalizer
{
    public static IReadOnlyList<FormReportCell> Normalize(string componentType, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return [];
        return componentType switch
        {
            "taxId" or "file" => [],
            "yesNo" or "checkbox" => Boolean(value),
            "number" or "currency" => Number(value),
            "date" => Date(value),
            "select" or "radio" or "text" or "textarea" or "email" or "phone" => Text(value),
            "checkboxGroup" => MultiText(value),
            _ => [],
        };
    }

    private static IReadOnlyList<FormReportCell> Boolean(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.True => [new FormReportCell("Yes", null, null)],
            JsonValueKind.False => [new FormReportCell("No", null, null)],
            _ => [],
        };

    private static IReadOnlyList<FormReportCell> Number(JsonElement value)
    {
        var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (
            string.IsNullOrWhiteSpace(raw)
            || !decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
        )
            return [];
        return [new FormReportCell(number.ToString(CultureInfo.InvariantCulture), number, null)];
    }

    private static IReadOnlyList<FormReportCell> Date(JsonElement value)
    {
        var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (
            string.IsNullOrWhiteSpace(raw)
            || !DateOnly.TryParseExact(
                raw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
        )
            return [];
        return [new FormReportCell(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, date)];
    }

    private static IReadOnlyList<FormReportCell> Text(JsonElement value)
    {
        var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? [] : [new FormReportCell(trimmed, null, null)];
    }

    private static IReadOnlyList<FormReportCell> MultiText(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return [];
        var cells = new List<FormReportCell>();
        foreach (var item in value.EnumerateArray())
        {
            var trimmed = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
            if (!string.IsNullOrEmpty(trimmed))
                cells.Add(new FormReportCell(trimmed, null, null));
        }
        return cells;
    }
}
