using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>
/// Deriva la referencia de pago (<c>PaymentId</c>) que liga cada reserva de código de Growth a un
/// onboarding. El stacking permite N reservas por onboarding, pero Growth exige <c>(Source, PaymentId)</c>
/// único (índice <c>UX_CodeReservations_Payment</c>), así que NO se puede reusar el OnboardingId para
/// todas: se deriva un GUID determinístico por <c>(OnboardingId, Order)</c>. Reserve y commit deben usar
/// el MISMO valor para la misma posición — por eso es determinístico (sin estado extra que persistir).
/// </summary>
public static class OnboardingPaymentReference
{
    public static Guid For(Guid onboardingId, int order)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"onb-payref:{onboardingId:N}:{order}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}

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

    /// <summary>Reserva (hold atómico) el código cotizado. <paramref name="paymentReferenceId"/> = GUID
    /// ÚNICO por reserva del mismo onboarding (Growth exige (Source,PaymentId) único vía
    /// UX_CodeReservations_Payment); derivarlo con <see cref="OnboardingPaymentReference"/>. TTL = vida del checkout.</summary>
    Task<Result<GrowthReserveResult>> ReserveAsync(
        Guid quoteId,
        Guid paymentReferenceId,
        int ttlSeconds,
        string idempotencyKey,
        CancellationToken ct = default
    );

    /// <summary>Confirma la redención de una reserva (al completarse la operación comercial). Idempotente.
    /// <paramref name="paymentReferenceId"/> DEBE coincidir con el usado en <see cref="ReserveAsync"/>.</summary>
    Task<Result> CommitAsync(
        Guid reservationId,
        Guid paymentReferenceId,
        string snapshotHash,
        Guid sourceEventId,
        string idempotencyKey,
        CancellationToken ct = default
    );

    /// <summary>Libera una reserva (checkout cancelado explícitamente). Idempotente.
    /// <paramref name="paymentReferenceId"/> DEBE coincidir con el usado en <see cref="ReserveAsync"/>
    /// (Growth valida <c>reservation.Payment == (Onboarding, paymentReferenceId)</c>); derivarlo con
    /// <see cref="OnboardingPaymentReference"/> por orden de código.</summary>
    Task<Result> CancelAsync(
        Guid reservationId,
        Guid paymentReferenceId,
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
