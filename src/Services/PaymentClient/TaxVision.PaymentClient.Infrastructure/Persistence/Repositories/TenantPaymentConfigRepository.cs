using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class TenantPaymentConfigRepository(PaymentClientDbContext db) : ITenantPaymentConfigRepository
{
    public Task<TenantPaymentConfig?> GetByTenantAndProviderAsync(Guid tenantId, PaymentProviderCode code, CancellationToken ct = default) =>
        WithEndpoints(db.TenantPaymentConfigs)
            .FirstOrDefaultAsync(config => config.TenantId == tenantId && config.ProviderCode == code, ct);

    public Task<TenantPaymentConfig?> GetByIdAsync(Guid tenantPaymentConfigId, Guid tenantId, CancellationToken ct = default) =>
        WithEndpoints(db.TenantPaymentConfigs)
            .FirstOrDefaultAsync(config => config.Id == tenantPaymentConfigId && config.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<TenantPaymentConfig>> GetActiveByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await WithEndpoints(db.TenantPaymentConfigs)
            .Where(config => config.TenantId == tenantId && config.IsActive)
            .ToListAsync(ct);

    public async Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default) =>
        await db.TenantPaymentConfigs.AddAsync(config, ct);

    private static IQueryable<TenantPaymentConfig> WithEndpoints(IQueryable<TenantPaymentConfig> query) =>
        query.Include(config => config.WebhookEndpoints);
}
