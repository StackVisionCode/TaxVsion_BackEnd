using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Abstractions;

public interface ITenantLogoMissingNotificationRepository
{
    Task<TenantLogoMissingNotification?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantLogoMissingNotification notification, CancellationToken ct = default);
}
