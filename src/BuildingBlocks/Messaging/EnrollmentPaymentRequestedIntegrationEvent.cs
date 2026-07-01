namespace BuildingBlocks.Messaging;

public sealed record EnrollmentPaymentRequestedIntegrationEvent : IntegrationEvent
{
    public Guid EnrollmentId { get; init; }
    public long AmountCents { get; init; }
    public string Currency { get; init; } = "USD";
    public string AdminEmail { get; init; } = string.Empty;
}
