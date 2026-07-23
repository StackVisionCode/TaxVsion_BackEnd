using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class TenantRecurringPaymentRepository(PaymentClientDbContext db) : ITenantRecurringPaymentRepository
{
    // IgnoreQueryFilters: este repo corre dentro de un handler de Wolverine (bus.InvokeAsync),
    // en un scope de DI distinto al de la request HTTP que pobló ITenantContext vía
    // JwtTenantContextMiddleware; el HasQueryFilter ambiental de PaymentClientDbContext ve
    // Guid.Empty ahí. tenantId ya viene explícito y validado desde el controller/evento.
    public Task<TenantRecurringPayment?> GetByIdAsync(
        Guid tenantRecurringPaymentId,
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        db
            .TenantRecurringPayments.IgnoreQueryFilters()
            .Include(plan => plan.Schedules)
            .FirstOrDefaultAsync(plan => plan.Id == tenantRecurringPaymentId && plan.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<TenantRecurringPayment>> SearchByTenantAsync(
        Guid tenantId,
        Guid? taxpayerId,
        RecurringStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db
            .TenantRecurringPayments.AsNoTracking()
            .IgnoreQueryFilters()
            .Include(plan => plan.Schedules)
            .Where(plan => plan.TenantId == tenantId);

        if (taxpayerId is not null)
            query = query.Where(plan => plan.TaxpayerId == taxpayerId);

        if (status is not null)
            query = query.Where(plan => plan.Status == status);

        return await query
            .OrderByDescending(plan => plan.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TenantRecurringPayment>> GetWithDueSchedulesAsync(
        RecurringScheduleStatus scheduleStatus,
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    )
    {
        var dueScheduleIds = await db.Set<RecurringSchedule>()
            .Where(schedule =>
                schedule.Status == scheduleStatus
                && (
                    scheduleStatus == RecurringScheduleStatus.Pending
                        ? schedule.ScheduledDate <= cutoffUtc
                        : schedule.NextRetryAtUtc <= cutoffUtc
                )
            )
            .OrderBy(schedule => schedule.ScheduledDate)
            .Select(schedule => schedule.TenantRecurringPaymentId)
            .Distinct()
            .Take(batchSize)
            .ToListAsync(ct);

        // IgnoreQueryFilters: job cross-tenant (RBAC Fase 5) — dueScheduleIds ya viene de
        // RecurringSchedule (no ITenantOwned, sin filtro), así que esta query también debe
        // ignorar el filtro para no perder planes de otros tenants con schedules vencidos.
        return await db
            .TenantRecurringPayments.IgnoreQueryFilters()
            .Include(plan => plan.Schedules)
            .Where(plan => plan.Status == RecurringStatus.Active && dueScheduleIds.Contains(plan.Id))
            .ToListAsync(ct);
    }

    public async Task AddAsync(TenantRecurringPayment plan, CancellationToken ct = default) =>
        await db.TenantRecurringPayments.AddAsync(plan, ct);
}
