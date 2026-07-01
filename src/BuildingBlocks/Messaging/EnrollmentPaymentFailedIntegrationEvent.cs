namespace BuildingBlocks.Messaging;

public sealed record EnrollmentPaymentFailedIntegrationEvent : IntegrationEvent
{
    public Guid EnrollmentId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
