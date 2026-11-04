using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Api.Requests;

/// <summary>
/// Parámetros comunes de los listados y de <c>/search</c>: rango de fechas (ISO, inclusivo) y filtros de cliente. Un
/// formato de fecha inválido ya lo rechaza el model binding (400); acá se valida el orden y se normaliza a UTC.
/// </summary>
internal static class MailListParams
{
    public static readonly Error InvalidDateRange = new(
        "Correspondence.InvalidDateRange",
        "fromUtc must be earlier than or equal to toUtc."
    );

    public static readonly Error CustomerFilterConflict = new(
        "Correspondence.CustomerFilterConflict",
        "Pass customerId or unlinked=true, not both."
    );

    public static bool TryDateRange(DateTime? from, DateTime? to, out DateTime? fromUtc, out DateTime? toUtc)
    {
        fromUtc = ToUtc(from);
        toUtc = ToUtc(to);
        return fromUtc is null || toUtc is null || fromUtc <= toUtc;
    }

    public static MailListFilter Filter(string? q, bool hasAttachments, DateTime? fromUtc, DateTime? toUtc) =>
        new(SearchTerm.Normalize(q), hasAttachments, fromUtc, toUtc);

    // Sin zona = UTC (el contrato dice "Utc"); con zona se convierte.
    private static DateTime? ToUtc(DateTime? value) =>
        value switch
        {
            null => null,
            { Kind: DateTimeKind.Utc } utc => utc,
            { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
            { } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
        };
}
