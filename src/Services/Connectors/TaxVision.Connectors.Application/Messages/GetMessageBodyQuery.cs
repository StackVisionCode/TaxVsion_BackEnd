namespace TaxVision.Connectors.Application.Messages;

public sealed record GetMessageBodyQuery(Guid TenantId, Guid AccountId, string ProviderMessageId);
