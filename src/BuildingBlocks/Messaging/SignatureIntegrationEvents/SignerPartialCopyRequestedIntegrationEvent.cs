namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// F7 — se emite cuando el aggregate decide que un firmante entra en la audiencia de la copia
/// parcial, justo después de que firme. El consumer rendea el PDF decorado, lo sube a CloudStorage
/// y lo entrega por el canal del firmante.
/// </summary>
public sealed record SignerPartialCopyRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }
    public required DateTime SignedAtUtc { get; init; }

    /// <summary>
    /// Clave estable `signature.partial_copy:{reqId}:{signerId}:v{n}` — el inbox del consumer
    /// deduplica contra ella. `v{n}` arranca en 1 y crece cada vez que el preparador pide reenvío.
    /// </summary>
    public required string IdempotencyKey { get; init; }
}
