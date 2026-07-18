namespace TaxVision.Connectors.Api.Requests;

public sealed record GetMessageBodyRequest(Guid TenantId, Guid AccountId);
