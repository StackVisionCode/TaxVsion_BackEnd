using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Search;

public sealed record SearchMailQuery(MailSearchCriteria Criteria);

/// <summary>
/// Búsqueda unificada tipo Outlook: correos (asunto, remitente, vista previa, destinatarios), archivos (nombre del
/// adjunto) y personas (corresponsales) en todas las carpetas, con el mismo control de acceso que el Inbox.
/// Solo calcula los grupos pedidos. HTTP-triggered.
/// </summary>
public static class SearchMailHandler
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public static async Task<MailSearchResult> Handle(
        SearchMailQuery query,
        IMailSearchRepository search,
        IConnectorsClient connectors,
        ILogger<SearchMailQuery> logger,
        CancellationToken ct
    )
    {
        var criteria = query.Criteria with { Limit = ClampLimit(query.Criteria.Limit) };
        criteria = await WithMailboxesAsync(criteria, connectors, logger, ct);

        var emails = criteria.Wants(MailSearchGroups.Emails) ? await search.SearchEmailsAsync(criteria, ct) : [];
        var files = criteria.Wants(MailSearchGroups.Files) ? await search.SearchFilesAsync(criteria, ct) : [];
        var people = criteria.Wants(MailSearchGroups.People) ? await search.SearchPeopleAsync(criteria, ct) : [];
        return new MailSearchResult(emails, files, people);
    }

    public static int ClampLimit(int requested) =>
        requested switch
        {
            < 1 => DefaultLimit,
            > MaxLimit => MaxLimit,
            _ => requested,
        };

    // Buzones del tenant solo si hacen falta (from= o personas). Si Connectors falla se degrada: lo saliente no
    // coincide por from= y personas no excluye buzones propios (no es un hueco: todo sale de correo ya visible).
    private static async Task<MailSearchCriteria> WithMailboxesAsync(
        MailSearchCriteria criteria,
        IConnectorsClient connectors,
        ILogger logger,
        CancellationToken ct
    )
    {
        var needed =
            (
                criteria.From is not null
                && (criteria.Wants(MailSearchGroups.Emails) || criteria.Wants(MailSearchGroups.Files))
            ) || criteria.Wants(MailSearchGroups.People);
        if (!needed)
            return criteria;

        var mailboxes = await connectors.GetMailboxAddressesAsync(criteria.TenantId, ct);
        if (mailboxes.IsFailure)
        {
            logger.LogWarning(
                "Could not load mailbox addresses for tenant {TenantId}; search runs without them: {Error}",
                criteria.TenantId,
                mailboxes.Error.Code
            );
            return criteria with { SenderAccountIds = [], OwnMailboxAddresses = [] };
        }

        var from = criteria.From;
        return criteria with
        {
            SenderAccountIds = from is null
                ? []
                : mailboxes
                    .Value.Where(m => Contains(m.EmailAddress, from) || Contains(m.DisplayName, from))
                    .Select(m => m.AccountId)
                    .ToList(),
            OwnMailboxAddresses = mailboxes
                .Value.Select(m => m.EmailAddress.Trim().ToLowerInvariant())
                .Distinct()
                .ToList(),
        };
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
