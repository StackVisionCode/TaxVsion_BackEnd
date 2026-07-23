using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class TenantPaymentConfigRepository(PaymentClientDbContext db) : ITenantPaymentConfigRepository
{
    public Task<TenantPaymentConfig?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    ) =>
        WithEndpoints(db.TenantPaymentConfigs)
            .FirstOrDefaultAsync(config => config.TenantId == tenantId && config.ProviderCode == code, ct);

    public Task<TenantPaymentConfig?> GetByIdAsync(
        Guid tenantPaymentConfigId,
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        WithEndpoints(db.TenantPaymentConfigs)
            .FirstOrDefaultAsync(config => config.Id == tenantPaymentConfigId && config.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<TenantPaymentConfig>> GetActiveByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await WithEndpoints(db.TenantPaymentConfigs)
            .Where(config => config.TenantId == tenantId && config.IsActive)
            .ToListAsync(ct);

    public async Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default) =>
        await db.TenantPaymentConfigs.AddAsync(config, ct);

    // IgnoreQueryFilters: los 3 métodos de arriba corren dentro de un handler de Wolverine
    // (bus.InvokeAsync), en un scope de DI distinto al de la request HTTP que pobló
    // ITenantContext vía JwtTenantContextMiddleware; el HasQueryFilter ambiental de
    // PaymentClientDbContext ve Guid.Empty ahí. tenantId ya viene explícito y validado en
    // cada uno de esos métodos, así que ignorar el filtro ambiental roto es seguro acá.
    private static IQueryable<TenantPaymentConfig> WithEndpoints(IQueryable<TenantPaymentConfig> query) =>
        query.IgnoreQueryFilters().Include(config => config.WebhookEndpoints);
}
