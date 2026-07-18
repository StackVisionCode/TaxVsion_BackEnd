namespace TaxVision.Connectors.Application.Accounts;

/// <summary><c>GET /connectors/accounts</c> (D3 §12.4).</summary>
public sealed record ListTenantEmailAccountsQuery(Guid TenantId);
