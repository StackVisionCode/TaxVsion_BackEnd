using BuildingBlocks.Domain;

namespace TaxVision.PaymentApp.Domain.Billing;

/// <summary>
/// Proyección local del <c>TenantBillingPreference</c> que vive en Subscription — mismo patrón que
/// <c>TenantPlanCodeProjection</c>: el consumer del evento de renovación la lee localmente para
/// decidir si hay consent para cobrar off-session, sin ir contra Subscription en cada ciclo.
/// </summary>
public sealed class TenantBillingPreferenceProjection : TenantEntity
{
    public const string ModeAutomatic = "Automatic";
    public const string ModeManual = "Manual";

    private TenantBillingPreferenceProjection() { }

    public string Mode { get; private set; } = ModeManual;
    public string? ProviderCode { get; private set; }
    public bool HasActiveMandate { get; private set; }
    public long? AgreedAmountCents { get; private set; }
    public string? AgreedCurrency { get; private set; }
    public string? AgreedCadence { get; private set; }

    /// <summary>Monotónico; ApplyIfNewer ignora eventos fuera de orden.</summary>
    public long RevisionTicks { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantBillingPreferenceProjection Create(
        Guid tenantId,
        string mode,
        string? providerCode,
        bool hasActiveMandate,
        long? agreedAmountCents,
        string? agreedCurrency,
        string? agreedCadence,
        long revisionTicks,
        DateTime updatedAtUtc
    )
    {
        var projection = new TenantBillingPreferenceProjection
        {
            Id = Guid.NewGuid(),
            Mode = mode,
            ProviderCode = providerCode,
            HasActiveMandate = hasActiveMandate,
            AgreedAmountCents = agreedAmountCents,
            AgreedCurrency = agreedCurrency,
            AgreedCadence = agreedCadence,
            RevisionTicks = revisionTicks,
            UpdatedAtUtc = updatedAtUtc,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void ApplyIfNewer(
        string mode,
        string? providerCode,
        bool hasActiveMandate,
        long? agreedAmountCents,
        string? agreedCurrency,
        string? agreedCadence,
        long revisionTicks,
        DateTime updatedAtUtc
    )
    {
        if (revisionTicks < RevisionTicks)
            return;
        Mode = mode;
        ProviderCode = providerCode;
        HasActiveMandate = hasActiveMandate;
        AgreedAmountCents = agreedAmountCents;
        AgreedCurrency = agreedCurrency;
        AgreedCadence = agreedCadence;
        RevisionTicks = revisionTicks;
        UpdatedAtUtc = updatedAtUtc;
    }

    public bool AllowsOffSessionCharge =>
        string.Equals(Mode, ModeAutomatic, StringComparison.Ordinal)
        && HasActiveMandate
        && !string.IsNullOrWhiteSpace(ProviderCode);
}
