namespace TaxVision.Subscription.Domain.Subscriptions;

public enum SubscriptionStatus
{
    Draft,
    Trialing,
    Active,
    PastDue,
    Suspended,
    Cancelled,
    Expired,
}
