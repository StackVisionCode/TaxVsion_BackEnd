namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Qué renovación se intentó cobrar en <see cref="RenewalChargeFailedIntegrationEvent"/>.</summary>
public static class RenewalChargeKinds
{
    public const string Plan = "Plan";
    public const string Seat = "Seat";
    public const string AddOn = "AddOn";
}
