namespace TaxVision.Connectors.Infrastructure.Providers.Watch;

/// <summary>Endpoint público del webhook de Graph (Fase 7, GraphNotificationWebhookController) + secreto compartido para validar el <c>clientState</c> de cada notificación entrante.</summary>
public sealed class GraphWatchOptions
{
    public const string SectionName = "Connectors:Watch:Graph";

    public string NotificationUrl { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint de avisos de ciclo de vida (reautorizar, subscription borrada, notificaciones perdidas).
    /// Opcional: vacío crea la subscription sin él. Graph solo lo acepta al CREAR la subscription
    /// (un PATCH no lo agrega), así que las existentes lo toman al reconectar o al recrearse.
    /// </summary>
    public string LifecycleNotificationUrl { get; set; } = string.Empty;
    public string ClientState { get; set; } = string.Empty;
}
