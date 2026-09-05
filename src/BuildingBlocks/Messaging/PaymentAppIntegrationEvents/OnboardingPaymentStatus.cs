namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Contract status used by the Auth <-> PaymentApp onboarding reconcile HTTP boundary.
/// It deliberately lives in BuildingBlocks so neither service parses another service's domain enum names.
/// </summary>
public enum OnboardingPaymentStatus
{
    Pending = 1,
    Processing = 2,
    RequiresAction = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6,
    PartiallyRefunded = 7,
    Refunded = 8,
    ChargedBack = 9,
}
