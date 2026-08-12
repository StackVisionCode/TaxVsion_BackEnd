using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

// Reminder Fase 10 — lo leen consumers de Wolverine, que no tienen TenantContext ambiente (no hay
// request HTTP). Mismo criterio que TenantPlanCodeProjectionRepository: IgnoreQueryFilters()
// explícito, con el tenantId del evento como filtro real.
public sealed class UserEmailDirectoryRepository(NotificationDbContext db) : IUserEmailDirectoryRepository
{
    public async Task<UserEmailDirectoryEntry?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        await db
            .UserEmailDirectoryEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId, ct);

    public async Task AddAsync(UserEmailDirectoryEntry entry, CancellationToken ct = default) =>
        await db.UserEmailDirectoryEntries.AddAsync(entry, ct);
}
