namespace TaxVision.Correspondence.Application.Messages;

public sealed record GetMessageMetadataQuery(Guid TenantId, Guid IncomingEmailId);
