namespace TaxVision.Connectors.Application.Messages;

public sealed record GetMessageAttachmentQuery(
    Guid TenantId,
    Guid AccountId,
    string ProviderMessageId,
    string AttachmentId
);
