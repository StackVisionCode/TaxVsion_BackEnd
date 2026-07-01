using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Subscriptions.Dtos;

public sealed class ChangePlanRequest
{
    public Guid? SubscriptionId { get; init; }
    public Guid? NewPlanId { get; init; }
    public BillingPeriod? NewBillingPeriod { get; init; }
    public string? GiftCardCode { get; init; }
}

