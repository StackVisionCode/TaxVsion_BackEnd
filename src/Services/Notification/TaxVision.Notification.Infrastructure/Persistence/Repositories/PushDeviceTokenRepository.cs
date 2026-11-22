using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class PushDeviceTokenRepository(NotificationDbContext db) : IPushDeviceTokenRepository
{
    public async Task AddAsync(PushDeviceToken token, CancellationToken ct = default) =>
        await db.PushDeviceTokens.AddAsync(token, ct);

    public async Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
        await db
            .PushDeviceTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Token == token, ct);

    public async Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        await db
            .PushDeviceTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .PushDeviceTokens.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive)
            .ToListAsync(ct);

    public async Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        // Fetch tracked a propósito (sin AsNoTracking, a diferencia de ListActiveForUserAsync) —
        // Revoke() necesita que EF detecte el cambio para que el SaveChangesAsync del caller lo
        // persista.
        var token = await db
            .PushDeviceTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);
        token?.Revoke();
    }

    public async Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        // Tracked: Revoke() se persiste con el SaveChangesAsync del caller.
        var tokens = await db
            .PushDeviceTokens.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke();
        return tokens.Count;
    }

    public Task<int> RevokeForSessionAsync(
        Guid tenantId,
        Guid userId,
        Guid sessionId,
        CancellationToken ct = default
    ) =>
        RevokeTrackedAsync(
            db.PushDeviceTokens.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive && t.SessionId == sessionId),
            ct
        );

    public Task<int> RevokeAllActiveForUserExceptSessionAsync(
        Guid tenantId,
        Guid userId,
        Guid? exceptSessionId,
        CancellationToken ct = default
    ) =>
        RevokeTrackedAsync(
            db.PushDeviceTokens.IgnoreQueryFilters()
                .Where(t =>
                    t.TenantId == tenantId
                    && t.UserId == userId
                    && t.IsActive
                    && (exceptSessionId == null || t.SessionId == null || t.SessionId != exceptSessionId)
                ),
            ct
        );

    // Entre oficinas a propósito: filtra por el token exacto y solo desactiva.
    public Task<int> DeactivateTokenInOtherTenantsAsync(
        string token,
        Guid exceptTenantId,
        CancellationToken ct = default
    ) =>
        RevokeTrackedAsync(
            db.PushDeviceTokens.IgnoreQueryFilters()
                .Where(t => t.Token == token && t.TenantId != exceptTenantId && t.IsActive),
            ct
        );

    // Tracked: Revoke() se persiste con el SaveChangesAsync del caller.
    private static async Task<int> RevokeTrackedAsync(IQueryable<PushDeviceToken> query, CancellationToken ct)
    {
        var tokens = await query.ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke();
        return tokens.Count;
    }
}
