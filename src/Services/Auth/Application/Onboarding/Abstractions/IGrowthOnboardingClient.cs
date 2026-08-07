using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>
/// Puerto M2M hacia Growth para aplicar códigos (promo/gift) y calificar referidos DURANTE el onboarding
/// (pre-tenant). Contrato pre-tenant aprobado: dueño = <c>PlatformTenant.Id</c>, sujeto =
/// <c>Anonymous(OnboardingId)</c>, referencia de pago = <c>("Onboarding", OnboardingId)</c> en ambos
/// carriles. Los endpoints <c>/internal/*</c> de Growth NO están expuestos por el gateway.
/// </summary>
public interface IGrowthOnboardingClient
{
    /// <summary>Cotiza un código contra el bruto residual (no consuntivo). Falla con
    /// <c>Growth.Quote.CodeNotFound</c> si el código no existe/no aplica → el caller lo reporta al usuario.</summary>
    Task<Result<GrowthQuoteResult>> QuoteAsync(GrowthQuoteRequest request, CancellationToken ct = default);

    /// <summary>Reserva (hold atómico) el código cotizado, ligado al OnboardingId. TTL = vida del checkout.</summary>
    Task<Result<GrowthReserveResult>> ReserveAsync(
        Guid quoteId,
        Guid onboardingId,
        int ttlSeconds,
        string idempotencyKey,
        CancellationToken ct = default
    );

    /// <summary>Confirma la redención de una reserva (al completarse la operación comercial). Idempotente.</summary>
    Task<Result> CommitAsync(
        Guid reservationId,
        Guid onboardingId,
        string snapshotHash,
        Guid sourceEventId,
        string idempotencyKey,
        CancellationToken ct = default
    );

    /// <summary>Libera una reserva (checkout cancelado/expirado). Idempotente.</summary>
    Task<Result> CancelAsync(
        Guid reservationId,
        Guid onboardingId,
        string reason,
        string idempotencyKey,
        CancellationToken ct = default
    );

    /// <summary>Califica al referido tras el primer pago exitoso (solo net &gt; 0). Idempotente.</summary>
    Task<Result> QualifyReferralAsync(GrowthQualifyRequest request, CancellationToken ct = default);
}

/// <summary>Cotización de un código contra el bruto residual. La oferta = el plan de suscripción.</summary>
public sealed record GrowthQuoteRequest(
    string CodeToken,
    Guid OnboardingId,
    Guid PlanId,
    string PlanVersion,
    long GrossAmountCents,
    string Currency,
    string SnapshotHash,
    int TtlSeconds
);

public sealed record GrowthQuoteResult(
    Guid QuoteId,
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    DateTime ExpiresAtUtc
);

public sealed record GrowthReserveResult(
    Guid ReservationId,
    long DiscountAmountCents,
    long NetAmountCents,
    DateTime ExpiresAtUtc
);

public sealed record GrowthQualifyRequest(
    Guid AttributionId,
    Guid QualifyingEventId,
    Guid PaymentId,
    long PaymentAmountCents,
    string PaymentCurrency,
    bool IsFirstSuccessfulPayment,
    DateTime PaymentSucceededAtUtc
);
