using System.Net;
using Stripe;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

/// <summary>Errores de Stripe que no dicen si el cargo salió (D-PAY-13).</summary>
internal static class StripeChargeFailure
{
    private const string IdempotencyError = "idempotency_error";
    private const string IdempotencyKeyInUse = "idempotency_key_in_use";

    /// <summary>5xx, sin respuesta, o la clave chocó con otra petición: el cargo pudo crearse. Un 4xx lo rechazó.</summary>
    public static bool IsOutcomeUnknown(StripeException ex) =>
        ex.HttpStatusCode == default
        || ex.HttpStatusCode >= HttpStatusCode.InternalServerError
        || ex.StripeError?.Type == IdempotencyError
        || ex.StripeError?.Code is IdempotencyError or IdempotencyKeyInUse;
}
