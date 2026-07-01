namespace TaxVision.Subscription.Domain.Subscriptions;
public enum SubscriptionStatus { Trialing, Active, PastDue, Suspended, CancelAtPeriodEnd, Cancelled }
public enum SeatStatus { PendingPayment, Active, PastDue, CancelAtPeriodEnd, Cancelled }
