using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Red de seguridad: si el período venció hace más de 2 días y el cobro nunca salió, la suscripción se
/// suspende. Sin esto una renovación perdida dejaba la oficina activa sin pagar para siempre.
/// </summary>
public sealed class OverdueRenewalSuspensionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<OverdueRenewalSuspensionJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(10))
{
    private const int BatchSize = 200;

    /// <summary>Más que la escalera de reintentos de PaymentApp (1 h + 6 h + 24 h).</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromDays(2);

    protected override string JobName => "overdue-renewal-suspension";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<OverdueRenewalSuspensionJob>>();

        var nowUtc = DateTime.UtcNow;
        var overdue = await subscriptions.GetActiveWithPeriodEndedBeforeAsync(nowUtc - Tolerance, BatchSize, ct);
        var suspended = 0;
        foreach (var subscription in overdue)
        {
            var previousStatus = subscription.Status;
            var result = subscription.SuspendBecauseRenewalOverdue(Tolerance, Guid.Empty, nowUtc);
            if (result.IsFailure)
                continue;

            await unitOfWork.SaveChangesAsync(ct);

            // Igual que la expiración: el recálculo corre en otro scope y necesita el tenant estampado.
            bus.TenantId = subscription.TenantId.ToString();
            await bus.RecalculateEntitlementsSafelyAsync(subscription.TenantId, logger, ct);
            await bus.PublishStatusChangedAsync(subscription, previousStatus, SubscriptionChangeReason.RenewalOverdue);
            await bus.PublishRenewalChargesSupersededAsync(subscription.TenantId, result.Value, string.Empty);
            suspended++;

            logger.LogWarning(
                "Suspended subscription {SubscriptionId} of tenant {TenantId}: period ended {PeriodEndUtc} and was never renewed.",
                subscription.Id,
                subscription.TenantId,
                subscription.CurrentPeriodEndUtc
            );
        }

        if (suspended > 0)
            logger.LogInformation("OverdueRenewalSuspensionJob suspended {Count} subscription(s).", suspended);
    }
}
