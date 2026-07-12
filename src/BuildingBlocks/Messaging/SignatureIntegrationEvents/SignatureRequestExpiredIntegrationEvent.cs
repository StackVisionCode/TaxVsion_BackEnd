namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// La solicitud pasó su <c>ExpiresAtUtc</c> sin completarse. Emitido por el
/// scheduler background; deja el aggregate en estado terminal <c>Expired</c>. Los
/// tokens vigentes quedan invalidados vía incremento de <c>RevocationEpoch</c>.
/// </summary>
public sealed record SignatureRequestExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required DateTime ExpiredAtUtc { get; init; }
    public required int RevocationEpoch { get; init; }
    public required IReadOnlyList<Guid> PendingSignerIds { get; init; }
}
