using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Billing;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class TenantBillingPreferenceRepository(SubscriptionDbContext db) : ITenantBillingPreferenceRepository
{
    // IgnoreQueryFilters + guard explícito del tenant: Wolverine resuelve el handler en un scope nuevo
    // donde el tenantContext del filter global queda vacío y descartaría la fila legítima. Aquí el
    // tenantId viene del comando (TryGetTenantAndUser del controller), fail-closed si llega vacío.
    public Task<TenantBillingPreference?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .BillingPreferences.IgnoreQueryFilters()
            .Include(preference => preference.Mandates)
            .FirstOrDefaultAsync(preference => preference.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBillingPreference preference, CancellationToken ct = default) =>
        await db.BillingPreferences.AddAsync(preference, ct);
}
