using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Layouts;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class EmailLayoutRepository(NotificationDbContext db) : IEmailLayoutRepository
{
    public async Task AddAsync(EmailLayout layout, CancellationToken ct = default) =>
        await db.EmailLayouts.AddAsync(layout, ct);

    public async Task<EmailLayout?> GetByIdAsync(Guid id, Guid? tenantId, CancellationToken ct = default)
    {
        var layout = await db.EmailLayouts.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (layout is null)
            return null;

        if (layout.Scope == EmailScope.Tenant && layout.TenantId != tenantId)
            return null;

        return layout;
    }

    public async Task<IReadOnlyList<EmailLayout>> ListAsync(
        Guid? tenantId,
        bool includeSystem,
        CancellationToken ct = default
    ) =>
        await db
            .EmailLayouts.AsNoTracking()
            .Where(l =>
                (tenantId != null && l.Scope == EmailScope.Tenant && l.TenantId == tenantId)
                || (includeSystem && l.Scope == EmailScope.System)
            )
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.LayoutName)
            .ToListAsync(ct);

    public async Task<EmailLayout?> GetDefaultAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenantDefault = await db
            .EmailLayouts.AsNoTracking()
            .Where(l => l.Scope == EmailScope.Tenant && l.TenantId == tenantId && l.IsDefault && l.IsActive)
            .OrderByDescending(l => l.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (tenantDefault is not null)
            return tenantDefault;

        return await db
            .EmailLayouts.AsNoTracking()
            .Where(l => l.Scope == EmailScope.System && l.IsDefault && l.IsActive)
            .OrderByDescending(l => l.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task ClearDefaultsAsync(EmailScope scope, Guid? tenantId, CancellationToken ct = default)
    {
        var defaults = await db
            .EmailLayouts.Where(l => l.Scope == scope && l.TenantId == tenantId && l.IsDefault)
            .ToListAsync(ct);

        foreach (var layout in defaults)
            layout.UnsetDefault();
    }
}
