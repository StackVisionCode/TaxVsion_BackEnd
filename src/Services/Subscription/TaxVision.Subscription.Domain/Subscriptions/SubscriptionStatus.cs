namespace TaxVision.Subscription.Domain.Subscriptions;

public enum SubscriptionStatus
{
    Draft,
    Trialing,
    Active,
    PastDue,
    GracePeriod,
    Suspended,
    Cancelled,
    Expired,
}
