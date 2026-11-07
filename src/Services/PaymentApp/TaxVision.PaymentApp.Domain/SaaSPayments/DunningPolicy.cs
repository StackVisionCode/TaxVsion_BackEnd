namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>Lo que decidió <see cref="DunningPolicy"/> para un cobro que falló.</summary>
public readonly record struct DunningDecision(DateTime? NextRetryAtUtc)
{
    public static DunningDecision NoRetry => default;

    public static DunningDecision RetryAt(DateTime nextRetryAtUtc) => new(nextRetryAtUtc);

    public bool WillRetry => NextRetryAtUtc is not null;
}

/// <summary>Cuándo se reintenta un cobro que falló. Función pura: la hora la pone quien llama.</summary>
public static class DunningPolicy
{
    /// <summary>Intentos de un ciclo de cobro con dunning: el primero más los 3 reintentos (1 h, 6 h, 24 h).</summary>
    public const int MaxAttempts = 4;

    /// <summary>Mismo código que usa Stripe: Subscription ya lo trata como fallo permanente.</summary>
    public const string MissingPaymentMethodCode = "requires_payment_method";

    /// <summary>Fallos que no se arreglan reintentando: el tenant tiene que pagar a mano ("Pay now").</summary>
    private static readonly HashSet<string> PermanentFailureCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        MissingPaymentMethodCode,
        // PayPal no tiene cobro directo: un reintento automático nunca puede cobrar.
        "PayPal.Authorize.NotImplemented",
    };

    public static bool IsPermanentFailure(string? failureCode) =>
        failureCode is not null && PermanentFailureCodes.Contains(failureCode);

    /// <summary>Solo las renovaciones en background tienen dunning. El cambio de plan es un cargo interactivo (el
    /// fallo se reporta en el acto) y los checkouts hosteados los reintenta el propio usuario.</summary>
    public static bool SupportsDunning(SaaSPaymentType type) =>
        type is SaaSPaymentType.SubscriptionRenewal or SaaSPaymentType.SeatRenewal or SaaSPaymentType.AddOnRenewal;

    /// <summary>Backoff 1h → 6h → 24h → se abandona, según los intentos previos al que falló.</summary>
    public static DunningDecision Decide(SaaSPaymentType type, int previousAttempts, DateTime nowUtc)
    {
        if (!SupportsDunning(type))
            return DunningDecision.NoRetry;

        return previousAttempts switch
        {
            0 => DunningDecision.RetryAt(nowUtc.AddHours(1)),
            1 => DunningDecision.RetryAt(nowUtc.AddHours(6)),
            2 => DunningDecision.RetryAt(nowUtc.AddHours(24)),
            _ => DunningDecision.NoRetry,
        };
    }

    /// <summary>El cobro síncrono decide antes de registrar su intento; el webhook y la reconciliación, con el intento
    /// fallido ya registrado (<paramref name="failedAttemptRecorded"/>).</summary>
    public static DunningDecision Decide(SaaSPayment payment, DateTime nowUtc, bool failedAttemptRecorded = false) =>
        Decide(payment.Type, payment.Attempts.Count - (failedAttemptRecorded ? 1 : 0), nowUtc);

    /// <summary>Un fallo permanente no se reintenta, diga lo que diga el calendario.</summary>
    public static DunningDecision ForFailure(string failureCode, DunningDecision decision) =>
        IsPermanentFailure(failureCode) ? DunningDecision.NoRetry : decision;
}
