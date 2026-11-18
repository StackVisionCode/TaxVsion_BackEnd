using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Domain.Customers;

namespace TaxVision.Sms.Tests.Fakes;

/// <summary>Proyección de política en memoria. Vacía = todos los clientes con SMS encendido.</summary>
internal sealed class FakeCustomerContactPolicyRepository : ICustomerContactPolicyRepository
{
    public List<CustomerContactPolicyProjection> Store { get; } = [];

    public void SmsDisabled(Guid tenantId, Guid customerId) =>
        Store.Add(CustomerContactPolicyProjection.Create(tenantId, customerId, smsEnabled: false));

    public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult(Store.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId));

    public Task<IReadOnlyCollection<Guid>> GetSmsDisabledCustomerIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(
            Store
                .Where(r => r.TenantId == tenantId && !r.SmsEnabled && customerIds.Contains(r.CustomerId))
                .Select(r => r.CustomerId)
                .ToList()
        );

    public Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default)
    {
        Store.Add(row);
        return Task.CompletedTask;
    }

    public Task<bool> ReconcileIfOlderAsync(
        Guid tenantId,
        Guid customerId,
        bool smsEnabled,
        DateTime snapshotAtUtc,
        CancellationToken ct = default
    )
    {
        var row = Store.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId);
        if (row is null || row.UpdatedAtUtc >= snapshotAtUtc)
            return Task.FromResult(false);
        row.Reconcile(smsEnabled);
        return Task.FromResult(true);
    }
}
