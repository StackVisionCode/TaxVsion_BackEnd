namespace TaxVision.Correspondence.Application.Messages;

public sealed record GetMessageBodyQuery(Guid TenantId, Guid IncomingEmailId);
