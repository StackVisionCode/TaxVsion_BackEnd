namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Solo el subscriptionId hace falta — el detalle de qué cambió (changeType/resource) no se usa:
/// siempre se re-pull el delta completo desde el cursor persistido (misma estrategia que Gmail).
/// </summary>
public sealed record ProcessGraphNotificationCommand(string SubscriptionId);
