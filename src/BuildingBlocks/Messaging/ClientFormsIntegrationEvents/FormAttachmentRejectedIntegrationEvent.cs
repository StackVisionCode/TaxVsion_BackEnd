namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_attachment_rejected.v1</summary>
public sealed record FormAttachmentRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public Guid? ResponsibleUserId { get; init; }
    public string? CustomerDisplayName { get; init; }
    public required string Title { get; init; }
    public required Guid AttachmentId { get; init; }
    public required Guid FileId { get; init; }
    public required string FileExtension { get; init; }
    public required string ReasonCode { get; init; }
    public required Guid RequestedByUserId { get; init; }
}
