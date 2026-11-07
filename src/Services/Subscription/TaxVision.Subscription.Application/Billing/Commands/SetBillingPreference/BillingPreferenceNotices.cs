using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Billing;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Billing.Commands.SetBillingPreference;

/// <summary>
/// Avisos de un cambio de preferencia, en orden: el cambio siempre, el de modo si cambió, y el del mandato si nació.
/// </summary>
internal static class BillingPreferenceNotices
{
    public static IReadOnlyList<object> For(
        TenantBillingPreference preference,
        BillingMode? previousMode,
        bool hadActiveMandateBefore,
        Guid tenantId,
        string correlationId,
        DateTime nowUtc
    )
    {
        var active = preference.ActiveMandate();
        var notices = new List<object> { Changed(preference, active, correlationId, nowUtc) };

        if (previousMode is null || previousMode != preference.Mode)
            notices.Add(ModeChanged(preference, active, tenantId, correlationId));

        if (preference.Mode == BillingMode.Automatic && active is not null && !hadActiveMandateBefore)
            notices.Add(MandateCreated(active, tenantId, correlationId));

        return notices;
    }

    private static TenantBillingPreferenceChangedIntegrationEvent Changed(
        TenantBillingPreference preference,
        TenantPaymentMandate? active,
        string correlationId,
        DateTime nowUtc
    ) =>
        new()
        {
            TenantId = preference.TenantId,
            CorrelationId = correlationId,
            Mode = preference.Mode.ToString(),
            ProviderCode = preference.ProviderCode,
            HasActiveMandate = active is not null,
            AgreedAmountCents = active?.AgreedAmountCents,
            AgreedCurrency = active?.AgreedCurrency,
            AgreedCadence = active?.AgreedCadence,
            RevisionTicks = preference.UpdatedAtUtc.Ticks,
            ChangedAtUtc = nowUtc,
        };

    private static TenantSubscriptionStatusChangedIntegrationEvent ModeChanged(
        TenantBillingPreference preference,
        TenantPaymentMandate? active,
        Guid tenantId,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            TenantSubscriptionId = Guid.Empty,
            Status = "Active",
            PreviousStatus = "Active",
            Reason = nameof(SubscriptionChangeReason.BillingModeChanged),
            NewMode = preference.Mode.ToString(),
            Amount = FormatAmount(active?.AgreedAmountCents),
            CardBrand = null,
            CardLast4 = null,
            Cadence = active?.AgreedCadence,
        };

    private static TenantSubscriptionStatusChangedIntegrationEvent MandateCreated(
        TenantPaymentMandate active,
        Guid tenantId,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            TenantSubscriptionId = Guid.Empty,
            Status = "Active",
            PreviousStatus = "Active",
            Reason = nameof(SubscriptionChangeReason.MandateCreated),
            Amount = FormatAmount(active.AgreedAmountCents),
            Cadence = active.AgreedCadence,
        };

    private static string? FormatAmount(long? cents) =>
        cents is { } c ? (c / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : null;
}
