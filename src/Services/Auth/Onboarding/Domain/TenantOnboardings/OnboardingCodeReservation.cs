using BuildingBlocks.Domain;

namespace TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

/// <summary>Tipo de beneficio aplicado en el onboarding. Se mantienen SEPARADOS (referido/promo/gift):
/// cada uno es su propia reserva en Growth y su propia línea de ajuste en la factura de Billing.</summary>
public enum OnboardingBenefitType
{
    Referral,
    Promo,
    Gift,
}

/// <summary>Entrada para registrar una reserva de código aplicada (resultado de la reserva secuencial
/// en Growth). <see cref="DiscountCents"/> = magnitud aplicada por esta reserva contra el residual.</summary>
public sealed record OnboardingCodeReservationInput(
    Guid CodeReservationId,
    OnboardingBenefitType BenefitType,
    string? Code,
    long DiscountCents,
    string SnapshotHash,
    // Vencimiento de la reserva en Growth y la referencia de pago con la que se creó.
    DateTime? ExpiresAtUtc = null,
    Guid? PaymentReferenceId = null,
    // Código de N meses o para siempre: el descuento que sigue en la suscripción después del alta.
    OnboardingRecurringDiscount? Recurring = null,
    // Código enmascarado (PREFIJO...ÚLTIMOS4) para el recibo.
    string? CodeDisplay = null
);

/// <summary>Una fila de descuento del recibo del alta: cómo se lee y cuánto restó.</summary>
public sealed record OnboardingReceiptDiscount(string Label, long AmountCents);

/// <summary>
/// Una reserva de código de Growth aplicada a un onboarding (stacking). Entidad hija de
/// <see cref="TenantOnboarding"/>. Guarda lo mínimo para (a) hacer el commit/cancel en Growth al
/// finalizar, y (b) construir la línea de ajuste de la factura. El monto es la magnitud del descuento
/// aplicado por ESTA reserva (calculada secuencialmente contra el residual).
/// </summary>
public sealed class OnboardingCodeReservation : BaseEntity
{
    public Guid OnboardingId { get; private set; }

    /// <summary>Id de la reserva en Growth (CodeReservation).</summary>
    public Guid CodeReservationId { get; private set; }

    public OnboardingBenefitType BenefitType { get; private set; }

    /// <summary>Etiqueta/código visible (p.ej. "WELCOME100"); null para el descuento automático de referido.</summary>
    public string? Code { get; private set; }

    /// <summary>Magnitud del descuento aplicado por esta reserva (contra el residual). Siempre &gt; 0.</summary>
    public long DiscountCents { get; private set; }

    /// <summary>Snapshot congelado del quote (se revalida en el commit contra Growth).</summary>
    public string SnapshotHash { get; private set; } = default!;

    /// <summary>Orden de aplicación (0-based) — referido → promo → gift.</summary>
    public int Order { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Código enmascarado para el recibo (nunca el código en claro). Null en reservas viejas.</summary>
    public string? CodeDisplay { get; private set; }

    public const int CodeDisplayMaxLength = 40;

    /// <summary>Cómo se lee en el recibo: "Promo code SPRI...0420", "Gift card GIFT...7F2A" o "Referral discount".</summary>
    public string ReceiptLabel() =>
        BenefitType switch
        {
            OnboardingBenefitType.Referral => "Referral discount",
            OnboardingBenefitType.Gift => CodeDisplay is null ? "Gift card" : $"Gift card {CodeDisplay}",
            _ => CodeDisplay is null ? "Promo code" : $"Promo code {CodeDisplay}",
        };

    /// <summary>Hasta cuándo la retiene Growth. Null en reservas guardadas antes de registrarlo.</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    /// <summary>Referencia de pago (<c>PaymentId</c>) con la que se reservó en Growth; commit y cancelación usan la
    /// misma. Null en reservas viejas: se deriva por orden.</summary>
    public Guid? PaymentReferenceId { get; private set; }

    // Descuento recurrente del código (null en códigos de un solo cobro y en reservas viejas).
    public Guid? RecurringCodeDefinitionId { get; private set; }
    public int? RecurringPercentBasisPoints { get; private set; }
    public long? RecurringAmountCents { get; private set; }
    public string? RecurringCurrency { get; private set; }
    public string? RecurringDuration { get; private set; }
    public int? RecurringMonths { get; private set; }

    public OnboardingRecurringDiscount? Recurring =>
        RecurringCodeDefinitionId is { } codeId && RecurringDuration is { } duration
            ? new OnboardingRecurringDiscount(
                codeId,
                RecurringPercentBasisPoints,
                RecurringAmountCents,
                RecurringCurrency,
                duration,
                RecurringMonths
            )
            : null;

    /// <summary>Vida de las reservas guardadas sin vencimiento (la que pedía el alta).</summary>
    public static readonly TimeSpan LegacyLifetime = TimeSpan.FromHours(24);

    public bool IsExpired(DateTime nowUtc) => (ExpiresAtUtc ?? CreatedAtUtc.Add(LegacyLifetime)) <= nowUtc;

    private OnboardingCodeReservation() { }

    internal OnboardingCodeReservation(
        Guid onboardingId,
        Guid codeReservationId,
        OnboardingBenefitType benefitType,
        string? code,
        long discountCents,
        string snapshotHash,
        int order,
        DateTime nowUtc,
        DateTime? expiresAtUtc = null,
        Guid? paymentReferenceId = null,
        OnboardingRecurringDiscount? recurring = null,
        string? codeDisplay = null
    )
    {
        var display = string.IsNullOrWhiteSpace(codeDisplay) ? null : codeDisplay.Trim();
        CodeDisplay = display is { Length: > CodeDisplayMaxLength } ? display[..CodeDisplayMaxLength] : display;
        RecurringCodeDefinitionId = recurring?.CodeDefinitionId;
        RecurringPercentBasisPoints = recurring?.PercentBasisPoints;
        RecurringAmountCents = recurring?.AmountCents;
        RecurringCurrency = recurring?.Currency;
        RecurringDuration = recurring?.Duration;
        RecurringMonths = recurring?.Months;
        ExpiresAtUtc = expiresAtUtc;
        PaymentReferenceId = paymentReferenceId;
        OnboardingId = onboardingId;
        CodeReservationId = codeReservationId;
        BenefitType = benefitType;
        Code = code;
        DiscountCents = discountCents;
        SnapshotHash = snapshotHash;
        Order = order;
        CreatedAtUtc = nowUtc;
    }
}
