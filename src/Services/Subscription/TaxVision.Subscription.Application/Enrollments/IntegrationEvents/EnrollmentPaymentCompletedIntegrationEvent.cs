using BuildingBlocks.Messaging;

namespace TaxVision.Subscription.Application.Enrollments.IntegrationEvents;

public sealed record EnrollmentPaymentCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid EnrollmentId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required decimal AmountPaid { get; init; }
}
