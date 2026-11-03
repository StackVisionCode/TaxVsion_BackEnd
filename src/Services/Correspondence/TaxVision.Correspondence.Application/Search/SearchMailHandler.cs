using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Search;

public sealed record SearchMailQuery(MailSearchCriteria Criteria);

/// <summary>
/// Búsqueda unificada tipo Outlook: correos (asunto, remitente, vista previa, destinatarios) y archivos
/// (nombre del adjunto) en todas las carpetas, con el mismo control de acceso que el Inbox. HTTP-triggered.
/// </summary>
public static class SearchMailHandler
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public static async Task<MailSearchResult> Handle(
        SearchMailQuery query,
        IMailSearchRepository search,
        CancellationToken ct
    )
    {
        var criteria = query.Criteria with { Limit = ClampLimit(query.Criteria.Limit) };
        var emails = await search.SearchEmailsAsync(criteria, ct);
        var files = await search.SearchFilesAsync(criteria, ct);
        return new MailSearchResult(emails, files);
    }

    public static int ClampLimit(int requested) =>
        requested switch
        {
            < 1 => DefaultLimit,
            > MaxLimit => MaxLimit,
            _ => requested,
        };
}
