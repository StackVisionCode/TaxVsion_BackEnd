using System.Globalization;

namespace TaxVision.FinancialInsights.Application.Imports;

/// <summary>Claves del plan que lee Financial Insights.</summary>
public static class FinancialEntitlements
{
    public const string Module = "financial_insights";

    // El portal sube los archivos por el módulo de documentos.
    public const string DocumentsModule = "documents";

    public const string DocumentsPerMonthKey = "financial.documents_per_month";

    public static int? ParseDocumentsPerMonth(IReadOnlyDictionary<string, string>? values) =>
        values is not null
        && values.TryGetValue(DocumentsPerMonthKey, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit)
        && limit >= 0
            ? limit
            : null;
}

/// <summary>Visibilidad por asignación de clientes (mismo criterio que Notes/Tasks).</summary>
public sealed class FinancialCustomerVisibilityOptions
{
    public const string SectionName = "FinancialInsights:CustomerVisibility";

    /// <summary>Encendida: quien no tiene customers.view_all solo ve los clientes que tiene asignados.</summary>
    public bool Enabled { get; set; } = true;
}
