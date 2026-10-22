namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Qué se suspendió tras el último intento de <see cref="RenewalChargeFailedIntegrationEvent"/>.</summary>
public static class RenewalChargeConsequences
{
    public const string SubscriptionSuspended = "SubscriptionSuspended";
    public const string SeatSuspended = "SeatSuspended";
    public const string AddOnSuspended = "AddOnSuspended";
}
