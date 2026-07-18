namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Un firmante pendiente merece un recordatorio (vence pronto y no ha firmado). El
/// scheduler emite un evento por firmante; Notification lo consume para dispatch de
/// email/SMS según canal preferido del firmante. Signature no re-emite tokens — el
/// mismo enlace público sigue vigente.
/// </summary>
public sealed record SignatureRequestReminderDueIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public required string Language { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required int RemindersSent { get; init; }
    public required string PublicUrl { get; init; }
}
