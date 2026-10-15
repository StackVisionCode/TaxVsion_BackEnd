using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Billing;

/// <summary>
/// Evidencia legal de un consentimiento específico del tenant para cobros recurrentes
/// off-session. Es una fila histórica inmutable: nunca se edita el consent original —
/// se revoca y se crea otra. Entidad hija de <see cref="TenantBillingPreference"/>:
/// su configuración EF requiere <c>ValueGeneratedNever()</c> (guardrail persistencia §49).
/// </summary>
public sealed class TenantPaymentMandate : BaseEntity
{
    /// <summary>Versión de la copia textual que se mostró al usuario al aceptar — permite
    /// probar qué términos aceptó aunque la copia cambie después.</summary>
    public const string CurrentConsentTextVersion = "v1-2026-10";

    public Guid TenantBillingPreferenceId { get; private set; }
    public Guid TenantId { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public string ConsentText { get; private set; } = default!;
    public string ConsentTextVersion { get; private set; } = default!;
    public string ConsentIpAddress { get; private set; } = default!;
    public string? ConsentUserAgent { get; private set; }
    public Guid AcceptedByUserId { get; private set; }
    public DateTime AcceptedAtUtc { get; private set; }
    public long AgreedAmountCents { get; private set; }
    public string AgreedCurrency { get; private set; } = default!;
    public string AgreedCadence { get; private set; } = default!;
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public bool IsActive => !IsRevoked;

    private TenantPaymentMandate() { }

    internal static Result<TenantPaymentMandate> Create(
        Guid tenantBillingPreferenceId,
        Guid tenantId,
        string providerCode,
        string consentText,
        string consentIpAddress,
        string? consentUserAgent,
        Guid acceptedByUserId,
        long agreedAmountCents,
        string agreedCurrency,
        string agreedCadence,
        DateTime nowUtc
    )
    {
        if (tenantBillingPreferenceId == Guid.Empty)
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.InvalidParent", "TenantBillingPreferenceId is required.")
            );

        if (tenantId == Guid.Empty)
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.InvalidTenant", "TenantId is required.")
            );

        if (string.IsNullOrWhiteSpace(providerCode))
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.InvalidProvider", "ProviderCode is required for an automatic mandate.")
            );

        if (string.IsNullOrWhiteSpace(consentText))
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.MissingConsentText", "ConsentText is required for legal evidence.")
            );

        if (string.IsNullOrWhiteSpace(consentIpAddress))
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.MissingConsentIp", "ConsentIpAddress is required for legal evidence.")
            );

        if (acceptedByUserId == Guid.Empty)
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.InvalidActor", "AcceptedByUserId is required.")
            );

        if (agreedAmountCents <= 0)
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.InvalidAmount", "AgreedAmountCents must be positive.")
            );

        if (string.IsNullOrWhiteSpace(agreedCurrency))
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.InvalidCurrency", "AgreedCurrency is required.")
            );

        if (string.IsNullOrWhiteSpace(agreedCadence))
            return Result.Failure<TenantPaymentMandate>(
                new Error("PaymentMandate.InvalidCadence", "AgreedCadence is required.")
            );

        return Result.Success(
            new TenantPaymentMandate
            {
                TenantBillingPreferenceId = tenantBillingPreferenceId,
                TenantId = tenantId,
                ProviderCode = providerCode,
                ConsentText = consentText,
                ConsentTextVersion = CurrentConsentTextVersion,
                ConsentIpAddress = consentIpAddress,
                ConsentUserAgent = consentUserAgent,
                AcceptedByUserId = acceptedByUserId,
                AcceptedAtUtc = nowUtc,
                AgreedAmountCents = agreedAmountCents,
                AgreedCurrency = agreedCurrency,
                AgreedCadence = agreedCadence,
                IsRevoked = false,
                CreatedAtUtc = nowUtc,
            }
        );
    }

    internal Result Revoke(string reason, DateTime nowUtc)
    {
        if (IsRevoked)
            return Result.Success();

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("PaymentMandate.InvalidRevokeReason", "Revoke reason is required."));

        IsRevoked = true;
        RevokedAtUtc = nowUtc;
        RevokedReason = reason.Length > 500 ? reason[..500] : reason;
        return Result.Success();
    }
}
