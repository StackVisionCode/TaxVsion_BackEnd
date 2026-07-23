using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class UserNotificationPreferenceRepository(NotificationDbContext db)
    : IUserNotificationPreferenceRepository
{
    public async Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken ct = default
    )
    {
        var preference = await GetAsync(tenantId, userId, category, channel, ct);
        return preference?.Enabled ?? true;
    }

    public async Task<UserNotificationPreference?> GetAsync(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken ct = default
    ) =>
        await db
            .UserNotificationPreferences.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.UserId == userId && p.Category == category && p.Channel == channel,
                ct
            );

    public async Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .UserNotificationPreferences.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.UserId == userId)
            .ToListAsync(ct);

    public async Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
        await db.UserNotificationPreferences.AddAsync(preference, ct);
}
