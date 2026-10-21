namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Aviso de ciclo de vida de una subscription de Graph (<c>lifecycleNotificationUrl</c>), ya validado
/// y encolado igual que <see cref="Sync.GraphNotificationReceived"/>.
/// </summary>
/// <param name="LifecycleEvent"><c>reauthorizationRequired</c>, <c>subscriptionRemoved</c> o <c>missed</c>.</param>
public sealed record GraphLifecycleNotificationReceived(
    string SubscriptionId,
    string LifecycleEvent,
    string CorrelationId
);

/// <summary>Valores de <c>lifecycleEvent</c> que manda Graph.</summary>
public static class GraphLifecycleEvents
{
    /// <summary>Graph va a dejar de notificar si no se reautoriza pronto (por ejemplo, el token se acerca a vencer).</summary>
    public const string ReauthorizationRequired = "reauthorizationRequired";

    /// <summary>Graph borró la subscription (acceso revocado, contraseña cambiada, etc.).</summary>
    public const string SubscriptionRemoved = "subscriptionRemoved";

    /// <summary>Graph no pudo entregar una o más notificaciones.</summary>
    public const string Missed = "missed";
}
