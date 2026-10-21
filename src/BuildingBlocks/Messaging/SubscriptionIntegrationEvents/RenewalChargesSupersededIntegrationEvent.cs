namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Los cobros automáticos de esas claves ya no se deben reintentar: el tenant pagó a mano ("Pay now")
/// o la cuenta se suspendió y solo vuelve pagando a mano. PaymentApp los cancela para no cobrar de más.
/// </summary>
public sealed record RenewalChargesSupersededIntegrationEvent : IntegrationEvent
{
    /// <summary>Claves de idempotencia de los cobros automáticos que quedaron saldados.</summary>
    public required IReadOnlyList<string> IdempotencyKeys { get; init; }
}
