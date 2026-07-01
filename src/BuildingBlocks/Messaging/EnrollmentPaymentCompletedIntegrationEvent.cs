namespace BuildingBlocks.Messaging;

public sealed record EnrollmentPaymentCompletedIntegrationEvent : IntegrationEvent
{
    public Guid EnrollmentId { get; init; }
    public string StripePaymentIntentId { get; init; } = string.Empty;
}
