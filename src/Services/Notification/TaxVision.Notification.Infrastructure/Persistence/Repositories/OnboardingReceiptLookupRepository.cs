using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Onboarding;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

// Plain BaseEntity (no ITenantOwned) — el filtro global de tenant de NotificationDbContext no
// alcanza a esta tabla, así que a diferencia de UserPermissionsProjectionRepository no hace falta
// IgnoreQueryFilters() acá.
public sealed class OnboardingReceiptLookupRepository(NotificationDbContext db) : IOnboardingReceiptLookupRepository
{
    public async Task<OnboardingReceiptLookup?> GetByOnboardingIdAsync(
        Guid onboardingId,
        CancellationToken ct = default
    ) => await db.OnboardingReceiptLookups.FirstOrDefaultAsync(x => x.OnboardingId == onboardingId, ct);

    public async Task AddAsync(OnboardingReceiptLookup lookup, CancellationToken ct = default) =>
        await db.OnboardingReceiptLookups.AddAsync(lookup, ct);
}
