using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class CustomerContactPolicyRepository(NotificationDbContext context)
    : ICustomerContactPolicyRepository,
        ICustomerContactPolicySnapshotWriter
{
    /// <summary>
    /// <c>IgnoreQueryFilters()</c> + tenant explícito: igual que el resto del Directory. Los consumers
    /// corren en el scope de Wolverine sin actor autenticado, y el filtro ambiental devolvería cero.
    /// </summary>
    public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        context
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.CustomerId == customerId, ct);

    public async Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default) =>
        await context.CustomerContactPolicyProjections.AddAsync(row, ct);

    // UPDATE condicional en una sola sentencia: si un evento escribió después de pedir el snapshot, no se pisa.
    public async Task<bool> ReconcileIfOlderAsync(
        Guid tenantId,
        Guid customerId,
        bool emailEnabled,
        bool smsEnabled,
        string language,
        DateTime snapshotAtUtc,
        CancellationToken ct = default
    )
    {
        var normalized = CustomerContactPolicyProjection.NormalizeLanguage(language);
        var nowUtc = DateTime.UtcNow;
        var updated = await context
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.CustomerId == customerId && e.UpdatedAtUtc < snapshotAtUtc)
            .ExecuteUpdateAsync(
                row =>
                    row.SetProperty(e => e.EmailEnabled, emailEnabled)
                        .SetProperty(e => e.SmsEnabled, smsEnabled)
                        .SetProperty(e => e.Language, normalized)
                        .SetProperty(e => e.UpdatedAtUtc, nowUtc),
                ct
            );
        return updated > 0;
    }
}
