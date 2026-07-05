using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class EmailProviderConfigurationRepository(NotificationDbContext db) : IEmailProviderConfigurationRepository
{
    public async Task AddAsync(EmailProviderConfiguration configuration, CancellationToken ct = default) =>
        await db.EmailProviderConfigurations.AddAsync(configuration, ct);

    public async Task<EmailProviderConfiguration?> GetByIdAsync(Guid id, Guid? tenantId, CancellationToken ct = default)
    {
        // Tracked: los comandos que llaman a este método pueden mutar la entidad y guardar.
        var config = await db.EmailProviderConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (config is null)
            return null;

        // Aislamiento multitenant: las System son visibles para todos; las de tenant SOLO para su
        // dueño. Sin tenant en contexto se deniega el acceso a cualquier configuración de tenant.
        if (config.Scope == ProviderScope.Tenant && config.TenantId != tenantId)
            return null;

        return config;
    }

    public async Task<IReadOnlyList<EmailProviderConfiguration>> ListAsync(
        Guid? tenantId,
        bool includeSystem,
        CancellationToken ct = default
    )
    {
        var query = db.EmailProviderConfigurations.AsNoTracking().AsQueryable();

        query = query.Where(c =>
            (tenantId != null && c.Scope == ProviderScope.Tenant && c.TenantId == tenantId)
            || (includeSystem && c.Scope == ProviderScope.System)
        );

        return await query.OrderByDescending(c => c.IsDefault).ThenBy(c => c.DisplayName).ToListAsync(ct);
    }

    public async Task<EmailProviderConfiguration?> GetTenantDefaultAsync(Guid tenantId, CancellationToken ct = default) =>
        await db
            .EmailProviderConfigurations.AsNoTracking()
            .Where(c => c.Scope == ProviderScope.Tenant && c.TenantId == tenantId && c.IsDefault && c.IsActive)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<EmailProviderConfiguration?> GetSystemDefaultAsync(CancellationToken ct = default) =>
        await db
            .EmailProviderConfigurations.AsNoTracking()
            .Where(c => c.Scope == ProviderScope.System && c.IsDefault && c.IsActive)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task ClearDefaultsAsync(ProviderScope scope, Guid? tenantId, CancellationToken ct = default)
    {
        var defaults = await db
            .EmailProviderConfigurations.Where(c =>
                c.Scope == scope && c.TenantId == tenantId && c.IsDefault
            )
            .ToListAsync(ct);

        foreach (var config in defaults)
            config.UnsetDefault();
    }
}
