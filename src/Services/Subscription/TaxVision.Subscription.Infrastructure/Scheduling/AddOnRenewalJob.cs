using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>Publica el intent de cobro por cada add-on que llega a su NextRenewalAtUtc.
/// Independiente de la suscripción base y de los seats.</summary>
public sealed class AddOnRenewalJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<AddOnRenewalJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    private const string BaseEndedReason = "Base subscription ended";

    protected override string JobName => "addon-renewal";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var tenantAddOns = services.GetRequiredService<ITenantAddOnRepository>();
        var logger = services.GetRequiredService<ILogger<AddOnRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await tenantAddOns.GetDueForRenewalAsync(nowUtc, BatchSize, ct);
        if (due.Count == 0)
            return;

        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        var correlationId = Guid.NewGuid().ToString("N");
        await RenewAsync([.. due.Select(addOn => (addOn.Id, addOn.TenantId))], nowUtc, correlationId, ct);
        logger.LogInformation("AddOnRenewalJob processed {Count} due add-on(s).", due.Count);
    }

    internal Task<JobBatchSummary> RenewAsync(
        IReadOnlyList<(Guid TenantAddOnId, Guid TenantId)> addOns,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    ) => RunUnitsAsync(addOns, (addOn, unit, token) => RenewOneAsync(unit, addOn, nowUtc, correlationId, token), ct);

    // La renovación en Processing y su AddOnRenewalDue (el cobro) se confirman juntos o no se confirma nada.
    private static async Task<bool> RenewOneAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        (Guid TenantAddOnId, Guid TenantId) item,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        unit.UseTenant(item.TenantId);
        var logger = unit.Services.GetRequiredService<ILogger<AddOnRenewalJob>>();
        var addOn = await unit
            .Services.GetRequiredService<ITenantAddOnRepository>()
            .GetByIdAsync(item.TenantAddOnId, item.TenantId, ct);
        if (addOn is null)
            return false;

        // Co-terminación: la renovación llega hasta el próximo aniversario de la base, no por el
        // ciclo propio del add-on, para que no se desfase del plan.
        var subscription = await unit
            .Services.GetRequiredService<ISubscriptionRepository>()
            .GetByTenantIdAsync(item.TenantId, ct);
        if (subscription is null)
        {
            logger.LogWarning("Add-on {TenantAddOnId} has no base subscription; skipping renewal.", addOn.Id);
            return false;
        }

        // C5 — sin base que dé acceso no se cobra: si puede volver, se espera; si terminó, se cancela.
        var decision = ExtraBilling.Decide(subscription.Status);
        if (decision == ExtraBillingDecision.Pause)
            return false;

        if (decision == ExtraBillingDecision.Cancel)
        {
            if (addOn.CancelActive(BaseEndedReason, actorUserId: Guid.Empty, nowUtc).IsFailure)
                return false;

            // A6/A5.7 — cancelar aquí y no recalcular dejaba el módulo del add-on en el snapshot.
            await unit.PublishAsync(new RecalculateEntitlementsCommand(item.TenantId));
            return true;
        }

        // Un cambio de ciclo en curso o agendado lo aplica la base primero; el add-on renueva después.
        if (subscription.HoldsExtraRenewals(addOn.CurrentPeriodEndUtc))
            return false;

        var newPeriodEndUtc = subscription.NextCoTermEnd(addOn.CurrentPeriodEndUtc);

        var idempotencyKey = IdempotencyKeyFactory.AddOnRenewal(addOn.Id, addOn.CurrentPeriodEndUtc);
        var result = addOn.BeginRenewal(idempotencyKey, newPeriodEndUtc, actorUserId: Guid.Empty, nowUtc);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not begin renewal for add-on {TenantAddOnId}: {Code}.",
                addOn.Id,
                result.Error.Code
            );
            return false;
        }

        var amountCents = (long)Math.Round(addOn.UnitPrice.Amount * 100m, MidpointRounding.AwayFromZero);
        await unit.PublishAsync(
            new AddOnRenewalDueIntegrationEvent
            {
                TenantId = addOn.TenantId,
                CorrelationId = correlationId,
                TenantAddOnId = addOn.Id,
                AddOnCode = addOn.AddOnCode,
                PeriodStartUtc = addOn.CurrentPeriodEndUtc,
                PeriodEndUtc = newPeriodEndUtc,
                IdempotencyKey = idempotencyKey,
                AmountCents = amountCents,
                Currency = addOn.UnitPrice.Currency,
            }
        );
        unit.Services.GetRequiredService<ISubscriptionMetrics>().RecordAddOnBilled(addOn.AddOnCode, amountCents);
        return true;
    }
}
