using TaxVision.Tenant.Application.Tenants.Commands;
namespace TaxVision.Tenant.Application.Tenants.Abstractions;

public interface ITenantReadService
{
    Task<IReadOnlyList<TenantResponse>> GetPageAsync(
    int page,
    int size,
    CancellationToken ct = default);
}
