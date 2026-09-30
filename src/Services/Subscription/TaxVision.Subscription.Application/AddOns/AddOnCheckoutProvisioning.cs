using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.AddOns;

/// <summary>
/// Activa el add-on de una intención ya pagada. Lo comparten el consumer del webhook y el job de
/// reconciliación, para que ambos caminos dejen exactamente el mismo estado. No hace <c>SaveChanges</c> ni
/// recalcula entitlements: eso lo hace el llamador. Molde: <c>SeatCheckoutProvisioning</c>.
/// </summary>
public static class AddOnCheckoutProvisioning
{
    /// <summary>Devuelve el add-on activado, o null si la intención no se pudo aprovisionar.</summary>
    public static async Task<TenantAddOn?> TryProvisionAsync(
        AddOnPurchaseIntent intent,
        TenantSubscription subscription,
        AddOnDefinition definition,
        ITenantAddOnRepository tenantAddOns,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionMetrics metrics,
        string correlationId,
        string provisioningMethod,
        DateTime paidAtUtc,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (intent.Status == AddOnPurchaseIntentStatus.Provisioned)
            return null;

        if (intent.MarkPaid(paidAtUtc).IsFailure)
            return null;

        var addOnResult = TenantAddOn.Purchase(
            intent.TenantId,
            definition,
            intent.Quantity,
            intent.UnitPrice,
            intent.BillingCycle,
            intent.AutoRenew,
            intent.RequestedByUserId,
            nowUtc
        );
        if (addOnResult.IsFailure)
            return null;

        var addOn = addOnResult.Value;

        // El primer período termina con el de la base, igual que en la compra off-session.
        if (addOn.CoTermTo(subscription.CurrentPeriodEndUtc, intent.RequestedByUserId, nowUtc).IsFailure)
            return null;

        // El cargo inicial ya está cobrado por el checkout: queda asentado en el ledger como liquidado, no
        // se publica ningún cobro off-session.
        if (!SettleInitialCharge(addOn, intent, paidAtUtc, nowUtc))
            return null;

        if (intent.MarkProvisioned(addOn.Id, nowUtc).IsFailure)
            return null;

        await tenantAddOns.AddAsync(addOn, ct);

        metrics.RecordAddOnPurchased(addOn.AddOnCode);
        metrics.RecordAddOnBilled(addOn.AddOnCode, intent.ProratedTotalCents);

        await AuditEntryFactory.AppendAsync(
            audit,
            intent.TenantId,
            "TenantAddOn",
            addOn.Id,
            "AddOn.Purchased",
            intent.RequestedByUserId,
            correlationId,
            before: (object?)null,
            after: new
            {
                addOn.AddOnCode,
                addOn.Quantity,
                Status = addOn.Status.ToString(),
                BilledCents = intent.ProratedTotalCents,
                Method = provisioningMethod,
            },
            reason: null,
            nowUtc,
            ct
        );

        return addOn;
    }

    private static bool SettleInitialCharge(
        TenantAddOn addOn,
        AddOnPurchaseIntent intent,
        DateTime paidAtUtc,
        DateTime nowUtc
    )
    {
        var key = IdempotencyKeyFactory.AddOnCheckout(intent.Id);
        if (addOn.BeginInitialCharge(key, intent.RequestedByUserId, nowUtc).IsFailure)
            return false;

        var renewal = addOn.Renewals.FirstOrDefault(candidate => candidate.IdempotencyKey == key);
        return renewal is not null
            && addOn
                .CompleteRenewal(renewal.Id, intent.SaaSPaymentId?.ToString("N"), intent.RequestedByUserId, paidAtUtc)
                .IsSuccess;
    }
}
