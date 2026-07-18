namespace TaxVision.Connectors.Application.Accounts;

/// <summary><c>GET /connectors/accounts/{id}</c> (D3 §12.4).</summary>
public sealed record GetTenantEmailAccountQuery(Guid TenantId, Guid AccountId);
