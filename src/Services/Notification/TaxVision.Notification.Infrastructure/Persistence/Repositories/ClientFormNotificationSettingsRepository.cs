using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

internal sealed class ClientFormNotificationSettingsRepository(NotificationDbContext context)
    : IClientFormNotificationSettingsRepository
{
    // Sin fila (oficina que nunca tocó el ajuste) el push va encendido, igual que el default de Tasks.
    public async Task<bool> IsPushEnabledAsync(Guid tenantId, CancellationToken ct = default) =>
        await context
            .ClientFormNotificationSettingsProjections.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId)
            .Select(row => (bool?)row.PushNotificationsEnabled)
            .SingleOrDefaultAsync(ct)
        ?? true;

    public Task<Domain.Directory.ClientFormNotificationSettingsProjection?> GetAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        context
            .ClientFormNotificationSettingsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.TenantId == tenantId, ct);

    public Task AddAsync(
        Domain.Directory.ClientFormNotificationSettingsProjection row,
        CancellationToken ct = default
    ) => context.ClientFormNotificationSettingsProjections.AddAsync(row, ct).AsTask();
}
