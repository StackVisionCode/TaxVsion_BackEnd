namespace TaxVision.Connectors.Application.Accounts;

/// <summary><c>GET /connectors/accounts</c> (D3 §12.4). Los buzones del propio usuario + el de oficina si <paramref name="CanSeeOffice"/> (permiso office.read).</summary>
public sealed record ListTenantEmailAccountsQuery(Guid TenantId, Guid CallerUserId, bool CanSeeOffice = true);
