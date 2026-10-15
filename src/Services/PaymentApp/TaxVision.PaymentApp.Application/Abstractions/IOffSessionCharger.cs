using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Abstractions;

/// <summary>
/// Cobro off-session (sin interacción del pagador) usando un token de método guardado por el
/// provider. El token es opaco para el dominio: Stripe lo interpreta como payment_method id,
/// PayPal como billing agreement/vault token id. Application nunca lee ni parsea el token.
/// </summary>
public interface IOffSessionCharger
{
    /// <summary>Código del provider que el factory indexa (p.ej. "Stripe", "PayPal").</summary>
    string ProviderCode { get; }

    Task<Result<OffSessionChargeOutcome>> ChargeAsync(OffSessionChargeRequest request, CancellationToken ct);
}

/// <summary>
/// Intento de cobro off-session. <paramref name="ProviderToken"/> es el token opaco del método
/// de pago previamente guardado por el provider (p.ej. tras un checkout con
/// <see cref="HostedCheckoutRequest.StoreMethodForOffSession"/> en true).
/// </summary>
public sealed record OffSessionChargeRequest(
    Guid TenantId,
    Guid SaaSPaymentId,
    string ProviderCode,
    string ProviderToken,
    long AmountCents,
    string Currency,
    string IdempotencyKey,
    string PayerEmail
);

/// <summary>
/// <paramref name="ProviderPaymentReference"/> es la referencia del cargo real (PaymentIntent /
/// capture id) para reconciliar vía webhook. <paramref name="FailureCode"/> y
/// <paramref name="FailureMessage"/> solo aplican a Failed / RequiresUserAction.
/// </summary>
public sealed record OffSessionChargeOutcome(
    OffSessionChargeStatus Status,
    string? ProviderPaymentReference,
    string? FailureCode,
    string? FailureMessage
);

/// <summary>
/// <see cref="RequiresUserAction"/> cubre el caso PSD2/SCA en EU — el banco pidió 3DS y el
/// cobro off-session no procede en esta vuelta; el flow de dunning debe caer a flow manual.
/// </summary>
public enum OffSessionChargeStatus
{
    Succeeded,
    Failed,
    RequiresUserAction,
}
