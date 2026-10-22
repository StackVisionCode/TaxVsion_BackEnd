namespace TaxVision.Connectors.Application.Abstractions;

/// <summary>
/// Si el listener de buzones está escuchando una cuenta. Mientras escucha, la reconciliación puede
/// espaciarse: el listener avisa en el momento en que llega correo.
/// </summary>
public interface IMailboxListenerPresence
{
    /// <summary>La sesión de la cuenta sigue viva; vence sola si el listener muere.</summary>
    Task MarkListeningAsync(Guid accountId, TimeSpan ttl, CancellationToken ct = default);

    Task<bool> IsListeningAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// true como mucho una vez por <paramref name="interval"/>: la reconciliación de respaldo de una
    /// cuenta escuchada corre en ese ritmo en vez del normal.
    /// </summary>
    Task<bool> TryClaimSafetyNetSlotAsync(Guid accountId, TimeSpan interval, CancellationToken ct = default);
}
