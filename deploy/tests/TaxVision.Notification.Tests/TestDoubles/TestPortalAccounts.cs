using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Cuentas del portal en memoria; vacía por defecto (todos los destinatarios son staff).</summary>
internal sealed class TestPortalAccounts : ICustomerPortalAccountProjectionRepository
{
    public List<CustomerPortalAccountProjection> Rows { get; } = [];

    public Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId)?.UserId);

    public Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId));

    public Task<CustomerPortalAccountProjection?> GetByUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.UserId == userId));

    public Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default)
    {
        Rows.Add(projection);
        return Task.CompletedTask;
    }
}
