namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El staff extendió el vencimiento de una solicitud vigente. Los tokens públicos
/// existentes quedaron revocados y se emiten tokens nuevos con el nuevo <c>exp</c>.
/// </summary>
public sealed record SignatureRequestExpirationExtendedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid ExtendedByUserId { get; init; }
    public required int AdditionalHours { get; init; }
    public required DateTime NewExpiresAtUtc { get; init; }
    public required int RevocationEpoch { get; init; }
}
