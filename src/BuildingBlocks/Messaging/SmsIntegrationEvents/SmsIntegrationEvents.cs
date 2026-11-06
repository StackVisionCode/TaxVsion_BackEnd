using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SmsIntegrationEvents;

/// <summary>
/// Eventos de resultado del microservicio SMS. AGNÓSTICOS: no incluyen identificadores de Campaign,
/// Reservation, Invoice ni Payment. <see cref="SmsMessageAcceptedIntegrationEvent.SourceContext"/> es
/// opaco (solo correlación/observabilidad) — cualquier caller (OTP, reminders, campañas…) correlaciona
/// por <c>CorrelationId</c>/<c>SourceContext</c> sin que SMS conozca su dominio. TenantId/CorrelationId
/// vienen de <see cref="IntegrationEvent"/>.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.SmsIntegrationEvents.SmsMessageAcceptedIntegrationEvent")]
public sealed record SmsMessageAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
    public required Guid CustomerId { get; init; }
    public string? SourceContext { get; init; }
    public string? ProviderMessageId { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.SmsIntegrationEvents.SmsMessageDeliveredIntegrationEvent")]
public sealed record SmsMessageDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
    public required Guid CustomerId { get; init; }
    public string? SourceContext { get; init; }
    public string? ProviderMessageId { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.SmsIntegrationEvents.SmsMessageFailedIntegrationEvent")]
public sealed record SmsMessageFailedIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
    public required Guid CustomerId { get; init; }
    public string? SourceContext { get; init; }
    public string? ProviderMessageId { get; init; }
    public string? FailureCode { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.SmsIntegrationEvents.SmsMessageSuppressedIntegrationEvent")]
public sealed record SmsMessageSuppressedIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
    public required Guid CustomerId { get; init; }
    public string? SourceContext { get; init; }
}
