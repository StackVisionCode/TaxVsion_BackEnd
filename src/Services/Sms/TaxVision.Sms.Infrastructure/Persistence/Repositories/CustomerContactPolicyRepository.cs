using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Domain.Customers;

namespace TaxVision.Sms.Infrastructure.Persistence.Repositories;

/// <summary>
/// <c>IgnoreQueryFilters()</c> + tenant explícito: los consumers y el job corren sin actor autenticado y el
/// filtro ambiental devolvería cero filas.
/// </summary>
public sealed class CustomerContactPolicyRepository(SmsDbContext context) : ICustomerContactPolicyRepository
{
    public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        context
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.CustomerId == customerId, ct);

    public async Task<IReadOnlyCollection<Guid>> GetSmsDisabledCustomerIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    )
    {
        if (customerIds.Count == 0)
            return [];
        return await context
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.SmsEnabled && customerIds.Contains(e.CustomerId))
            .Select(e => e.CustomerId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default) =>
        await context.CustomerContactPolicyProjections.AddAsync(row, ct);

    // UPDATE condicional en una sola sentencia: si un evento escribió después de pedir el snapshot, no se pisa.
    public async Task<bool> ReconcileIfOlderAsync(
        Guid tenantId,
        Guid customerId,
        bool smsEnabled,
        DateTime snapshotAtUtc,
        CancellationToken ct = default
    )
    {
        var nowUtc = DateTime.UtcNow;
        var updated = await context
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.CustomerId == customerId && e.UpdatedAtUtc < snapshotAtUtc)
            .ExecuteUpdateAsync(
                row => row.SetProperty(e => e.SmsEnabled, smsEnabled).SetProperty(e => e.UpdatedAtUtc, nowUtc),
                ct
            );
        return updated > 0;
    }
}
