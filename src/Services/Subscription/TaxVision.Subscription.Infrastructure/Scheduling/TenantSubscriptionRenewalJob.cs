using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>Publica el intent de cobro cuando la suscripción base de un tenant llega a
/// NextRenewalAtUtc. No renueva seats ni add-ons. Si hay un downgrade agendado
/// (<see cref="PendingDowngrade"/>) para esta suscripción, lo aplica ANTES de resolver el
/// precio — así la renovación cobra normalmente el precio del plan nuevo, sin ningún job
/// separado ni prorrateo.</summary>
public sealed class TenantSubscriptionRenewalJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<TenantSubscriptionRenewalJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "tenant-subscription-renewal";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var logger = services.GetRequiredService<ILogger<TenantSubscriptionRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await subscriptions.GetDueForRenewalAsync(nowUtc, BatchSize, ct);
        if (due.Count == 0)
            return;

        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        var correlationId = Guid.NewGuid().ToString("N");
        await RenewAsync([.. due.Select(subscription => subscription.TenantId)], nowUtc, correlationId, ct);
        logger.LogInformation("TenantSubscriptionRenewalJob processed {Count} due subscription(s).", due.Count);
    }

    internal Task<JobBatchSummary> RenewAsync(
        IReadOnlyList<Guid> tenantIds,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    ) =>
        RunUnitsAsync(
            tenantIds,
            (tenantId, unit, token) => RenewOneAsync(unit, tenantId, nowUtc, correlationId, token),
            ct
        );

    // Una unidad por suscripción: downgrade agendado, renovación en Processing y SubscriptionRenewalDue (el cobro)
    // se confirman juntos. Si el precio no se resuelve, se confirma solo el downgrade.
    private static async Task<bool> RenewOneAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid tenantId,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        unit.UseTenant(tenantId);
        var logger = unit.Services.GetRequiredService<ILogger<TenantSubscriptionRenewalJob>>();
        var plans = unit.Services.GetRequiredService<IPlanRepository>();
        var subscription = await unit
            .Services.GetRequiredService<ISubscriptionRepository>()
            .GetByTenantIdAsync(tenantId, ct);
        if (subscription is null || subscription.Status != SubscriptionStatus.Active)
            return false;

        // Cancelación programada: llegó el fin del período, así que en vez de renovar se expira. Va
        // primero — no tiene sentido aplicar un downgrade ni resolver un precio de algo que termina acá.
        if (subscription.CancelAtPeriodEnd)
            return await ExpireScheduledCancellationAsync(unit, subscription, logger, nowUtc);

        var downgraded = await ApplyPendingDowngradeIfAnyAsync(
            unit,
            subscription,
            plans,
            logger,
            correlationId,
            nowUtc,
            ct
        );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var planVersion = PlanPricing.FindVersion(plan, subscription.PlanVersionId);
        var price = planVersion is null
            ? null
            : PlanPricing.ResolveBaseSubscriptionPrice(planVersion, subscription.BillingCycle);
        if (price is null)
        {
            logger.LogWarning(
                "Could not resolve a price tier for subscription {SubscriptionId} (plan {PlanId}, version {PlanVersionId}, cycle {BillingCycle}); skipping renewal intent.",
                subscription.Id,
                subscription.PlanId,
                subscription.PlanVersionId,
                subscription.BillingCycle
            );
            return downgraded;
        }

        var idempotencyKey = IdempotencyKeyFactory.SubscriptionRenewalFor(subscription);
        var result = subscription.BeginRenewal(idempotencyKey, actorUserId: Guid.Empty, nowUtc);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not begin renewal for subscription {SubscriptionId}: {Code}.",
                subscription.Id,
                result.Error.Code
            );
            return downgraded;
        }

        await unit.PublishAsync(
            new SubscriptionRenewalDueIntegrationEvent
            {
                TenantId = subscription.TenantId,
                CorrelationId = correlationId,
                TenantSubscriptionId = subscription.Id,
                PlanCode = subscription.PlanCode,
                PeriodStartUtc = subscription.CurrentPeriodEndUtc,
                PeriodEndUtc = subscription.BillingCycle.CalculateNext(subscription.CurrentPeriodEndUtc),
                IdempotencyKey = idempotencyKey,
                AmountCents = price.Value.AmountCents,
                Currency = price.Value.Currency,
            }
        );
        return true;
    }

    /// <summary>El tenant canceló y el período ya terminó: expira y recalcula el acceso; el aviso CancellationEnded
    /// sale del evento de dominio. Los seats y add-ons terminan solos con la base — sus jobs miran el estado.</summary>
    private static async Task<bool> ExpireScheduledCancellationAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        TenantSubscription subscription,
        ILogger logger,
        DateTime nowUtc
    )
    {
        var expired = subscription.ExpireAfterScheduledCancellation(actorUserId: Guid.Empty, nowUtc);
        if (expired.IsFailure)
        {
            logger.LogWarning(
                "Could not expire the scheduled cancellation of tenant {TenantId}: {Code}.",
                subscription.TenantId,
                expired.Error.Code
            );
            return false;
        }

        await unit.PublishAsync(new RecalculateEntitlementsCommand(subscription.TenantId));
        logger.LogInformation(
            "Subscription of tenant {TenantId} expired at the end of its period after a scheduled cancellation.",
            subscription.TenantId
        );
        return true;
    }

    private static async Task<bool> ApplyPendingDowngradeIfAnyAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        TenantSubscription subscription,
        IPlanRepository plans,
        ILogger logger,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var pendingDowngrade = subscription.PendingDowngrades.FirstOrDefault(d =>
            d.Status == PendingDowngradeStatus.Scheduled
        );
        if (pendingDowngrade is null)
            return false;

        var toPlan = await plans.GetByIdAsync(pendingDowngrade.ToPlanId, ct);
        var toPlanVersion = toPlan?.Versions.FirstOrDefault(v => v.Id == pendingDowngrade.ToPlanVersionId);
        if (toPlan is null || toPlanVersion is null)
        {
            logger.LogWarning(
                "Could not apply pending downgrade {Id} for subscription {SubscriptionId}: target plan/version no longer exists.",
                pendingDowngrade.Id,
                subscription.Id
            );
            return false;
        }

        var applyResult = subscription.ApplyPendingDowngrade(
            pendingDowngrade.Id,
            toPlan,
            toPlanVersion,
            actorUserId: Guid.Empty,
            nowUtc
        );
        if (applyResult.IsFailure)
        {
            logger.LogWarning(
                "Could not apply pending downgrade {Id} for subscription {SubscriptionId}: {Code}.",
                pendingDowngrade.Id,
                subscription.Id,
                applyResult.Error.Code
            );
            return false;
        }

        // Absorción: si el plan destino ya cubre el módulo del add-on, cancelarlo (sin doble cobro).
        await AddOnAbsorptionService.AbsorbCoveredByPlanAsync(
            subscription.TenantId,
            toPlanVersion,
            unit.Services.GetRequiredService<ITenantAddOnRepository>(),
            unit.Services.GetRequiredService<IAddOnDefinitionRepository>(),
            unit.Outbox,
            unit.Services.GetRequiredService<ISubscriptionMetrics>(),
            correlationId,
            actorUserId: Guid.Empty,
            nowUtc,
            logger,
            ct
        );

        await unit.PublishAsync(new RecalculateEntitlementsCommand(subscription.TenantId));
        return true;
    }
}
