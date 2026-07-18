using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Recurring.Commands.ExecuteRecurringSchedule;
using TaxVision.PaymentClient.Domain.Recurring;
using Wolverine;

namespace TaxVision.PaymentClient.Infrastructure.Scheduling;

/// <summary>
/// Ejecuta schedules <c>Pending</c> cuya <c>ScheduledDate</c> ya llegó — corrige los
/// anti-patrones del <c>RecurringPaymentProcessor</c> legacy del CRM (§6.7 del diseño): lock
/// distribuido (evita doble-cobro entre réplicas), <c>BatchSize</c> acotado, y delega el
/// timing de tenant timezone a que el caller de <c>CreateTenantRecurringPaymentCommand</c> ya
/// haya calculado <c>StartDate</c>/<c>ScheduledDate</c> en UTC correctamente.
/// </summary>
public sealed class TenantRecurringExecutionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<TenantRecurringExecutionJob> logger
) : PeriodicPaymentClientJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "tenant-recurring-execution";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var plans = services.GetRequiredService<ITenantRecurringPaymentRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var logger = services.GetRequiredService<ILogger<TenantRecurringExecutionJob>>();

        var due = await plans.GetWithDueSchedulesAsync(RecurringScheduleStatus.Pending, DateTime.UtcNow, BatchSize, ct);
        var processed = 0;

        foreach (var plan in due)
        {
            foreach (
                var schedule in plan.Schedules.Where(s =>
                    s.Status == RecurringScheduleStatus.Pending && s.ScheduledDate <= DateTime.UtcNow
                )
            )
            {
                var result = await bus.InvokeAsync<Result>(
                    new ExecuteRecurringScheduleCommand(plan.TenantId, plan.Id, schedule.Id),
                    ct
                );
                processed++;

                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "TenantRecurringExecutionJob failed to execute schedule {ScheduleId} of plan {PlanId}: {Code} — {Message}",
                        schedule.Id,
                        plan.Id,
                        result.Error.Code,
                        result.Error.Message
                    );
                }
            }
        }

        if (processed > 0)
            logger.LogInformation("TenantRecurringExecutionJob processed {Count} due schedule(s).", processed);
    }
}
