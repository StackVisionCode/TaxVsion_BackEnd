using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;

namespace TaxVision.Tenant.Application.Tenants.Queries;

public sealed record GetTenantsQuery(int Page = 1, int Size = 20);
public static class GetTenantsHandler
{
    public static Task<IReadOnlyList<TenantResponse>> Handle(
    GetTenantsQuery q,
    ITenantReadService reader,
    CancellationToken ct)
    => reader.GetPageAsync(q.Page, q.Size, ct);
}
