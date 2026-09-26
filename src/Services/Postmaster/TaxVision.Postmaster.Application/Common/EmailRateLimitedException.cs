namespace TaxVision.Postmaster.Application.Common;

/// <summary>
/// El envío está por encima del cupo del provider (o Connectors respondió 429): nada salió. Se lanza en
/// vez de marcar el mensaje Failed para que la transacción del handler se revierta —no queda
/// <c>SentMessage</c> ni reserva de idempotencia de este intento— y la política de Wolverine
/// (<c>Program.cs</c>) reprograme el mensaje tras <see cref="NextAttemptDelay"/>. Antes un email por
/// encima de 60/min por tenant quedaba como fallo permanente.
/// </summary>
public sealed class EmailRateLimitedException(string idempotencyKey, TimeSpan retryAfter)
    : Exception($"Email '{idempotencyKey}' is over the provider quota; retrying in {retryAfter.TotalSeconds:0}s.")
{
    /// <summary>Espera extra aleatoria: los diferidos no deben volver todos juntos al abrir la ventana.</summary>
    private static readonly TimeSpan MaxJitter = TimeSpan.FromSeconds(15);

    public string IdempotencyKey { get; } = idempotencyKey;

    public TimeSpan RetryAfter { get; } = retryAfter;

    public TimeSpan NextAttemptDelay() =>
        (RetryAfter > TimeSpan.Zero ? RetryAfter : TimeSpan.FromSeconds(1))
        + TimeSpan.FromMilliseconds(Random.Shared.Next((int)MaxJitter.TotalMilliseconds));
}
