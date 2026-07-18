using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeTenantLogoMissingNotificationRepository : ITenantLogoMissingNotificationRepository
{
    private readonly Dictionary<Guid, TenantLogoMissingNotification> _store = new();

    public Task<TenantLogoMissingNotification?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(tenantId, out var value) ? value : null);

    public Task AddAsync(TenantLogoMissingNotification notification, CancellationToken ct = default)
    {
        _store[notification.TenantId] = notification;
        return Task.CompletedTask;
    }
}
