using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Recurring.Commands.ExecuteRecurringSchedule;
using TaxVision.PaymentClient.Domain.Recurring;
using Wolverine;

namespace TaxVision.PaymentClient.Infrastructure.Scheduling;

/// <summary>
/// Contraparte del <c>RecurringPaymentRetryJob</c> legacy del CRM (§6.7 del diseño: "Nuevo: un
/// solo <c>PeriodicSubscriptionJob</c> base con dos configuraciones") — mismo
/// <see cref="PeriodicPaymentClientJob"/> base que <see cref="TenantRecurringExecutionJob"/>,
/// pero corre cada 24h y toma schedules <c>RetryPending</c> cuyo <c>NextRetryAtUtc</c> ya
/// venció, en vez de <c>Pending</c> por <c>ScheduledDate</c>.
/// </summary>
public sealed class TenantRecurringRetryJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<TenantRecurringRetryJob> logger
) : PeriodicPaymentClientJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(24), TimeSpan.FromHours(1))
{
    private const int BatchSize = 50;

    protected override string JobName => "tenant-recurring-retry";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var plans = services.GetRequiredService<ITenantRecurringPaymentRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var logger = services.GetRequiredService<ILogger<TenantRecurringRetryJob>>();

        var due = await plans.GetWithDueSchedulesAsync(
            RecurringScheduleStatus.RetryPending,
            DateTime.UtcNow,
            BatchSize,
            ct
        );
        var processed = 0;

        foreach (var plan in due)
        {
            foreach (
                var schedule in plan.Schedules.Where(s =>
                    s.Status == RecurringScheduleStatus.RetryPending && s.NextRetryAtUtc <= DateTime.UtcNow
                )
            )
            {
                // RBAC Fase 5 — bus.InvokeAsync crea un scope Wolverine nuevo; sin este stamp
                // LocalCommandTenantMiddleware no tiene tenant que restaurar y el filtro
                // fail-closed de PaymentClientDbContext bloquearía el handler.
                bus.TenantId = plan.TenantId.ToString();
                var result = await bus.InvokeAsync<Result>(
                    new ExecuteRecurringScheduleCommand(plan.TenantId, plan.Id, schedule.Id),
                    ct
                );
                processed++;

                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "TenantRecurringRetryJob failed to retry schedule {ScheduleId} of plan {PlanId}: {Code} — {Message}",
                        schedule.Id,
                        plan.Id,
                        result.Error.Code,
                        result.Error.Message
                    );
                }
            }
        }

        if (processed > 0)
            logger.LogInformation("TenantRecurringRetryJob processed {Count} due retry(ies).", processed);
    }
}
