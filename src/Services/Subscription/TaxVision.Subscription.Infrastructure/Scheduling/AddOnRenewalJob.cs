using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.TierChange;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Application.Subscriptions.Renewals;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;
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
        var usedBytes = await MeasureScheduledTierChangesAsync(services, due, ct);
        await RenewAsync([.. due.Select(addOn => (addOn.Id, addOn.TenantId))], nowUtc, correlationId, ct, usedBytes);
        logger.LogInformation("AddOnRenewalJob processed {Count} due add-on(s).", due.Count);
    }

    internal Task<JobBatchSummary> RenewAsync(
        IReadOnlyList<(Guid TenantAddOnId, Guid TenantId)> addOns,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct,
        IReadOnlyDictionary<Guid, long?>? usedBytesByTenant = null
    ) =>
        RunUnitsAsync(
            addOns,
            (addOn, unit, token) =>
                RenewOneAsync(
                    unit,
                    addOn,
                    usedBytesByTenant?.GetValueOrDefault(addOn.TenantId),
                    nowUtc,
                    correlationId,
                    token
                ),
            ct
        );

    // Bajadas programadas: el uso se mide aquí, antes de abrir las transacciones de cada unidad (BB-8).
    private static async Task<IReadOnlyDictionary<Guid, long?>> MeasureScheduledTierChangesAsync(
        IServiceProvider services,
        IReadOnlyList<TenantAddOn> due,
        CancellationToken ct
    )
    {
        var usage = services.GetRequiredService<IStorageUsageClient>();
        var used = new Dictionary<Guid, long?>();
        foreach (var tenantId in due.Where(a => a.HasScheduledTierChange).Select(a => a.TenantId).Distinct())
            used[tenantId] = await usage.GetUsedBytesAsync(tenantId, ct);
        return used;
    }

    // La renovación en Processing y su AddOnRenewalDue (el cobro) se confirman juntos o no se confirma nada.
    private static async Task<bool> RenewOneAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        (Guid TenantAddOnId, Guid TenantId) item,
        long? usedBytes,
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

        // Termina con el plan: lo renueva el job del plan en el mismo cobro.
        if (RenewalBundle.Includes(subscription, addOn))
            return false;

        // La bajada programada se decide antes de cobrar: el cobro sale ya con el tamaño que quede.
        var tierChange = await ResolveScheduledTierChangeAsync(unit, addOn, usedBytes, nowUtc, ct);
        if (tierChange == ScheduledTierChangeOutcome.Postponed)
        {
            logger.LogWarning("Renewal of add-on {TenantAddOnId} postponed: storage usage unavailable.", addOn.Id);
            return false;
        }

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

        // P-1: precio × cantidad, como dice el aviso de renovación.
        var grossCents =
            (long)Math.Round(addOn.UnitPrice.Amount * 100m, MidpointRounding.AwayFromZero) * addOn.Quantity;
        // El descuento vigente que cubre add-ons se mira con la fecha de inicio del período nuevo.
        var charge = DiscountedCharge.For(
            subscription,
            DiscountChargeKind.AddOns,
            grossCents,
            addOn.UnitPrice.Currency,
            addOn.CurrentPeriodEndUtc
        );
        var amountCents = charge.NetCents;
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
                DiscountAmountCents = charge.DiscountCents,
                DiscountSource = charge.Source,
                DiscountLabel = charge.Label,
            }
        );
        unit.Services.GetRequiredService<ISubscriptionMetrics>().RecordAddOnBilled(addOn.AddOnCode, amountCents);
        if (tierChange == ScheduledTierChangeOutcome.Applied)
            await unit.PublishAsync(new RecalculateEntitlementsCommand(item.TenantId));
        return true;
    }

    private static async Task<ScheduledTierChangeOutcome> ResolveScheduledTierChangeAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        TenantAddOn addOn,
        long? usedBytes,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (!addOn.HasScheduledTierChange)
            return ScheduledTierChangeOutcome.None;

        var definitions = unit.Services.GetRequiredService<IAddOnDefinitionRepository>();
        var current = await definitions.GetByIdAsync(addOn.AddOnDefinitionId, ct);
        var pending = await definitions.GetByIdAsync(addOn.PendingAddOnDefinitionId!.Value, ct);
        if (current is null || pending is null)
            return ScheduledTierChangeOutcome.Postponed;

        var maxBytes = await AddOnTierChangeStorage.CurrentMaxBytesAsync(
            addOn.TenantId,
            unit.Services.GetRequiredService<ITenantEntitlementSnapshotRepository>(),
            ct
        );
        return ScheduledTierChangeAtRenewal.Resolve(addOn, current, pending, usedBytes, maxBytes, nowUtc);
    }
}
