using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class PushDeviceTokenRepository(NotificationDbContext db) : IPushDeviceTokenRepository
{
    public async Task AddAsync(PushDeviceToken token, CancellationToken ct = default) =>
        await db.PushDeviceTokens.AddAsync(token, ct);

    public async Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
        await db.PushDeviceTokens.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Token == token, ct);

    public async Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        await db.PushDeviceTokens.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .PushDeviceTokens.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive)
            .ToListAsync(ct);

    public async Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        // Fetch tracked a propósito (sin AsNoTracking, a diferencia de ListActiveForUserAsync) —
        // Revoke() necesita que EF detecte el cambio para que el SaveChangesAsync del caller lo
        // persista.
        var token = await db.PushDeviceTokens.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);
        token?.Revoke();
    }
}
