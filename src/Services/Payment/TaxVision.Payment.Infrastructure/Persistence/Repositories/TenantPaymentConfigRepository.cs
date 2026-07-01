using Microsoft.EntityFrameworkCore;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.TenantPayments;
using TaxVision.Payment.Infrastructure.Persistence;

namespace TaxVision.Payment.Infrastructure.Persistence.Repositories;

public sealed class TenantPaymentConfigRepository(PaymentDbContext dbContext) : ITenantPaymentConfigRepository
{
    public async Task<TenantPaymentConfig?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
        => await dbContext.TenantPaymentConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default)
        => await dbContext.TenantPaymentConfigs.AddAsync(config, ct);
}
