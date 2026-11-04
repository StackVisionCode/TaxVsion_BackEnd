namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Registro automático del webhook de cada oficina. Apagado mientras <see cref="PublicBaseUrl"/> esté
/// vacía (local: Stripe/PayPal no llegan a localhost). La ruta sale del enrutado (ruta nombrada del
/// descriptor), esta base solo pone esquema y host públicos.
/// </summary>
public sealed class PaymentWebhookOptions
{
    public const string SectionName = "PaymentClient:Webhooks";

    /// <summary>Tope de <see cref="MaxAttempts"/>.</summary>
    public const int MaxAttemptsLimit = 10;

    /// <summary>Base pública https por la que el proveedor llega al webhook (p. ej. el host de la API).</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>false = no registrar aunque haya base pública.</summary>
    public bool AutoRegister { get; set; } = true;

    /// <summary>Intentos ante fallos pasajeros antes de marcar el registro como fallido.</summary>
    public int MaxAttempts { get; set; } = 3;

    public int RetryDelaySeconds { get; set; } = 60;

    /// <summary>Cada cuánto se revisan registros pendientes que se perdieron (caída entre guardar y encolar).</summary>
    public int PendingSweepIntervalMinutes { get; set; } = 10;

    /// <summary>Margen sobre el ciclo completo de reintentos antes de dar un pendiente por perdido.</summary>
    public int PendingMarginMinutes { get; set; } = 15;

    public int PendingSweepBatchSize { get; set; } = 50;

    /// <summary>Un pendiente más viejo que esto se perdió: supera todo el ciclo de reintentos
    /// (<see cref="MaxAttempts"/> × <see cref="RetryDelaySeconds"/>) más <see cref="PendingMarginMinutes"/>.</summary>
    public TimeSpan StalePendingAfter =>
        TimeSpan.FromSeconds((long)MaxAttempts * RetryDelaySeconds) + TimeSpan.FromMinutes(PendingMarginMinutes);

    public bool IsAutoSetupEnabled => AutoRegister && !string.IsNullOrWhiteSpace(PublicBaseUrl);
}
