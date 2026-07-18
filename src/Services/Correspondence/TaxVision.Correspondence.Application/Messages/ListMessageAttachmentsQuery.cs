namespace TaxVision.Correspondence.Application.Messages;

public sealed record ListMessageAttachmentsQuery(Guid TenantId, Guid IncomingEmailId);
