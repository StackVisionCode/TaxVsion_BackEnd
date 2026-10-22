namespace TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageDetail;

/// <param name="MaskRecipients">true para quien no es admin: ve el dominio, no la dirección completa.</param>
public sealed record GetSentMessageDetailQuery(Guid TenantId, Guid SentMessageId, bool MaskRecipients);
