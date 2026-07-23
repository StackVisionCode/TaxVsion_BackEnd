using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Payouts;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class PayoutScheduleRepository(PaymentClientDbContext db) : IPayoutScheduleRepository
{
    // IgnoreQueryFilters: este repo corre dentro de un handler de Wolverine (bus.InvokeAsync),
    // en un scope de DI distinto al de la request HTTP que pobló ITenantContext vía
    // JwtTenantContextMiddleware; el HasQueryFilter ambiental de PaymentClientDbContext ve
    // Guid.Empty ahí. tenantId ya viene explícito y validado desde el controller/evento.
    public Task<PayoutSchedule?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .PayoutSchedules.IgnoreQueryFilters()
            .Include(schedule => schedule.Items)
            .FirstOrDefaultAsync(schedule => schedule.TenantId == tenantId, ct);

    public Task<PayoutSchedule?> GetByTenantConnectAccountIdAsync(
        Guid tenantConnectAccountId,
        CancellationToken ct = default
    ) =>
        db
            .PayoutSchedules.Include(schedule => schedule.Items)
            .FirstOrDefaultAsync(schedule => schedule.TenantConnectAccountId == tenantConnectAccountId, ct);

    public async Task AddAsync(PayoutSchedule schedule, CancellationToken ct = default) =>
        await db.PayoutSchedules.AddAsync(schedule, ct);
}
