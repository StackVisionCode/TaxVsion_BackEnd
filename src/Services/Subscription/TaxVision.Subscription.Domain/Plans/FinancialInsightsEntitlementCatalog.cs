using System.Globalization;
using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Claves <c>financial.*</c> de Financial Insights. El tope mensual de documentos analizados con IA es por oficina:
/// cada análisis tiene costo, así que el plan lo acota y el administrador de plataforma lo ajusta por edición.
/// </summary>
public static class FinancialInsightsEntitlementCatalog
{
    public const string DocumentsPerMonth = "financial.documents_per_month";

    public static IReadOnlyList<string> Keys { get; } = [DocumentsPerMonth];

    public static IReadOnlyCollection<EntitlementSeed> Defaults { get; } =
    [
        new(DocumentsPerMonth, EntitlementValueType.Int, 200.ToString(CultureInfo.InvariantCulture)),
    ];
}
