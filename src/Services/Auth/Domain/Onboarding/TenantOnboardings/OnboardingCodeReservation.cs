using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

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
    string SnapshotHash
);

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

    private OnboardingCodeReservation() { }

    internal OnboardingCodeReservation(
        Guid onboardingId,
        Guid codeReservationId,
        OnboardingBenefitType benefitType,
        string? code,
        long discountCents,
        string snapshotHash,
        int order,
        DateTime nowUtc
    )
    {
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
