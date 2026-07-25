using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class TenantLogoMissingNotificationRepository(ScribeDbContext dbContext)
    : ITenantLogoMissingNotificationRepository
{
    // IgnoreQueryFilters: llamado exclusivamente desde el pipeline de render M2M (LogoResolver ←
    // RenderController, ActorType.Service — sin claim tenant_id, el ITenantContext ambiente nunca
    // refleja el tenant real del render). El aislamiento acá lo da el parámetro tenantId explícito
    // de la query, no el filtro global (RBAC Fase 5).
    public Task<TenantLogoMissingNotification?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext
            .TenantLogoMissingNotifications.IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.TenantId == tenantId, ct);

    public async Task AddAsync(TenantLogoMissingNotification notification, CancellationToken ct = default) =>
        await dbContext.TenantLogoMissingNotifications.AddAsync(notification, ct);
}
