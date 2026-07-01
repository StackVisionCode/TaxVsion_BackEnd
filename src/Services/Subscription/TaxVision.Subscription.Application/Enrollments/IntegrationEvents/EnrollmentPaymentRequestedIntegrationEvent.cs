using BuildingBlocks.Messaging;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Enrollments.IntegrationEvents;

public sealed record EnrollmentPaymentRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid EnrollmentId { get; init; }
    public required string PlanCode { get; init; }
    public required Guid PlanVersionId { get; init; }
    public required string AdminEmail { get; init; }
    public required string OrgName { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required BillingPeriod BillingPeriod { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
