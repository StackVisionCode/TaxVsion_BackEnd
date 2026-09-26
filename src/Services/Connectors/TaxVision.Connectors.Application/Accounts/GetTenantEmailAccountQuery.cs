namespace TaxVision.Connectors.Application.Accounts;

/// <summary><c>GET /connectors/accounts/{id}</c> (D3 §12.4). Un buzón propio del caller, o el de oficina si <paramref name="CanSeeOffice"/> (permiso office.read).</summary>
public sealed record GetTenantEmailAccountQuery(
    Guid TenantId,
    Guid AccountId,
    Guid CallerUserId,
    bool CanSeeOffice = true
);
