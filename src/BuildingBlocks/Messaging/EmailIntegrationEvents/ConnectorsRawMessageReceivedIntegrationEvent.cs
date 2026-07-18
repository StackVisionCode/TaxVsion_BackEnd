using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>Metadata de un attachment SIN bytes — nunca se descarga proactivamente (Connectors Fase 7, regla dura).</summary>
public sealed record ConnectorsRawMessageAttachmentMetadata(
    string Filename,
    string ContentType,
    long SizeBytes,
    string ProviderAttachmentId
);

/// <summary>
/// Connectors detectó un mensaje nuevo (Gmail push / Graph notification) y publica solo su
/// metadata — nunca el body ni bytes de attachments (regla dura, Connectors_Service_Design_And_Implementation_Plan.md §19).
/// Correspondence decide si lo persiste filtrando contra sus CustomerEmailAddresses; el body/attachments
/// reales se piden bajo demanda vía los endpoints M2M de Connectors (Fases 8/9).
/// SpfResult/DkimResult/DmarcResult son <c>AuthenticationSignals</c> de Connectors serializados como
/// string (Pass|Fail|None|Unknown) — BuildingBlocks no depende del Domain de ningún microservicio.
/// </summary>
[MessageIdentity("connectors.raw_message_received.v1")]
public sealed record ConnectorsRawMessageReceivedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required string ProviderCode { get; init; }
    public required string ProviderMessageId { get; init; }
    public string? ProviderThreadId { get; init; }
    public string? InternetMessageId { get; init; }
    public string? InReplyTo { get; init; }
    public IReadOnlyList<string>? References { get; init; }
    public required string From { get; init; }
    public required IReadOnlyList<string> To { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public required string Subject { get; init; }
    public required string Snippet { get; init; }
    public required DateTime ReceivedAtUtc { get; init; }
    public required bool HasAttachments { get; init; }
    public required int AttachmentCount { get; init; }
    public IReadOnlyList<ConnectorsRawMessageAttachmentMetadata>? AttachmentMetadata { get; init; }
    public required string SpfResult { get; init; }
    public required string DkimResult { get; init; }
    public required string DmarcResult { get; init; }
}
