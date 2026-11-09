using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// La retención borró la solicitud. Avisa a quien guarda su <c>SignatureRequestId</c> (Client Forms,
/// Notification); los consumidores son opcionales y hoy no hay ninguno.
/// </summary>
[MessageIdentity("signature.request.purged.v1")]
public sealed record SignatureRequestPurgedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required DateTime PurgedAtUtc { get; init; }
}
