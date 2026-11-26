using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;

/// <summary>Mantiene la proyección local al día para que <c>TenantStatusGateMiddleware</c>
/// pueda rechazar tenants Suspended/Closed sin ir a Auth.</summary>
public static class TenantStatusChangedConsumer
{
    private const string SuspendedSkipReason = "The tenant could not operate when this period was due.";

    // Tope de seguridad: las fechas siempre avanzan, así que el bucle termina antes.
    private const int MaxPeriodsToSkip = 5000;

    public static async Task Handle(
        TenantStatusChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        ITenantRecurringPaymentRepository plans,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var nowUtc = DateTime.UtcNow;
            var tenant = await tenants.GetByIdAsync(evt.ChangedTenantId, ct);
            // Sin proyección los cobros no se frenaron: no hay períodos que omitir.
            var couldOperate = tenant?.CanOperate() ?? true;

            await tenants.UpdateStatusAsync(
                evt.ChangedTenantId,
                evt.Status,
                evt.IsActive,
                nowUtc,
                evt.TenantVersion,
                ct
            );

            if (!couldOperate && tenant!.CanOperate())
                await SkipPeriodsDueWhileSuspendedAsync(plans, tenant.Id, nowUtc, ct);

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    // L2-01: al reactivar, los períodos que vencieron con la oficina suspendida se omiten y se cobra desde el siguiente.
    private static async Task SkipPeriodsDueWhileSuspendedAsync(
        ITenantRecurringPaymentRepository plans,
        Guid tenantId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        foreach (var plan in await plans.GetActiveWithDuePendingSchedulesByTenantAsync(tenantId, nowUtc, ct))
        {
            for (var i = 0; i < MaxPeriodsToSkip; i++)
            {
                var due = plan
                    .Schedules.Where(s => s.Status == RecurringScheduleStatus.Pending && s.ScheduledDate <= nowUtc)
                    .ToList();
                if (due.Count == 0)
                    break;

                foreach (var schedule in due)
                    plan.SkipSchedule(schedule.Id, SuspendedSkipReason, Guid.Empty, nowUtc);

                // Igual que tras un cobro: el siguiente se genera solo si no queda otro por delante.
                if (
                    plan.Schedules.Any(s =>
                        s.Status is RecurringScheduleStatus.Pending or RecurringScheduleStatus.RetryPending
                    )
                )
                    break;

                var generated = plan.GenerateSchedules(1, nowUtc);
                if (generated.IsFailure || generated.Value.Count == 0)
                    break;
            }
        }
    }
}
