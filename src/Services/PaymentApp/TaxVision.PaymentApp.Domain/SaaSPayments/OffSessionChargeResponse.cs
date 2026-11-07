using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

public enum OffSessionChargeResult
{
    Succeeded,
    RequiresUserAction,

    /// <summary>Sin resultado final: el cargo pudo salir (D-PAY-13). Sin dunning ni clave nueva.</summary>
    NoFinalOutcome,
    Declined,
}

/// <summary>Lo que respondió un cobro off-session, en términos del dominio. <paramref name="Reference"/> es null si el
/// provider no devolvió una referencia válida; <paramref name="ResponseCode"/> queda en el intento.</summary>
public sealed record OffSessionChargeResponse(
    OffSessionChargeResult Result,
    ExternalPaymentReference? Reference,
    string ResponseCode,
    string? FailureCode,
    string? FailureMessage
);

/// <summary>Qué le pasó al pago. <paramref name="FailureCode"/> con valor: quedó Failed con ese código.</summary>
public readonly record struct OffSessionChargeApplied(
    bool ProviderConfirmed = false,
    string? FailureCode = null,
    bool NeedsUserAction = false,
    bool NoFinalOutcome = false
);
