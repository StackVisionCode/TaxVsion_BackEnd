using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// Un buzón de OFICINA quedó ligado a un empleado retirado (es su dirección, o lo creó/conectó él).
/// Connectors NO lo desconecta: lo marca para revisión y deja el canal al consumidor, que avisa a quienes
/// tengan <see cref="AudiencePermission"/>. Resolverlo = desconectarlo o volver a autorizarlo.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.ConnectorsIntegrationEvents.ConnectorsOfficeMailboxReviewRequiredIntegrationEvent")]
public sealed record ConnectorsOfficeMailboxReviewRequiredIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required string EmailAddress { get; init; }
    public required string ProviderCode { get; init; }

    /// <summary>"AddressOfOffboardedUser" o "ConnectedByOffboardedUser".</summary>
    public required string Reason { get; init; }
    public required Guid OffboardedUserId { get; init; }

    /// <summary>Permiso de la audiencia a avisar (quienes administran los buzones de oficina).</summary>
    public required string AudiencePermission { get; init; }
    public required DateTime FlaggedAtUtc { get; init; }
}
