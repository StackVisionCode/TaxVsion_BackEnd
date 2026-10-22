namespace TaxVision.Postmaster.Application.Sending.Queries.ListSentMessages;

/// <param name="MaskRecipients">true para quien no es admin: ve el dominio, no la dirección completa.</param>
public sealed record ListSentMessagesQuery(
    Guid TenantId,
    SentMessageActivityFilter Filter,
    int Page,
    int Size,
    bool MaskRecipients
);
