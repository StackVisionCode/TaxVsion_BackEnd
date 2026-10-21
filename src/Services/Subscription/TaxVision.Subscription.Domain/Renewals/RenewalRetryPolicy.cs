namespace TaxVision.Subscription.Domain.Renewals;

/// <summary>
/// Cuándo un cobro de renovación fallido se vuelve a intentar. No se confía solo en el WillRetry de
/// PaymentApp: un fallo permanente o demasiados reintentos cortan el ciclo aunque diga que reintenta.
/// </summary>
public static class RenewalRetryPolicy
{
    public const int MaxRetryAttempts = 3;

    // Sin tarjeta guardada reintentar no sirve: el tenant tiene que pagar desde la página de billing.
    private static readonly HashSet<string> PermanentFailureCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "requires_payment_method",
        "PayPal.Authorize.NotImplemented",
    };

    public static bool ShouldRetry(bool providerWillRetry, int retryCount, string failureCode) =>
        providerWillRetry && retryCount < MaxRetryAttempts && !PermanentFailureCodes.Contains(failureCode);
}
