namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>
/// task.form_participant_notification_requested.v1 (P-14.7). Aviso por email a un participante externo
/// sin vincular: el equivalente de los avisos al cliente, que salen del hecho. Es un comando punto a
/// punto a Notification porque lleva el email del externo, y ese dato nunca va por el fanout.
/// </summary>
public sealed record FormParticipantNotificationRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid CommandId { get; init; }
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }

    /// <summary>Uno de <see cref="FormParticipantNotificationKinds"/>.</summary>
    public required string Kind { get; init; }

    public required ProtectedPayload Payload { get; init; }
}

/// <summary>Params: solo texto inocuo (contadores, extensión, códigos). Nunca respuestas ni nombres de archivo.</summary>
public sealed record FormParticipantNotificationPayload(
    string Email,
    string? Name,
    string Title,
    IReadOnlyDictionary<string, string> Params,
    string Language = "En"
);

public static class FormParticipantNotificationKinds
{
    public const string Returned = "returned";
    public const string Reminder = "reminder";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string AttachmentRejected = "attachment_rejected";
}
