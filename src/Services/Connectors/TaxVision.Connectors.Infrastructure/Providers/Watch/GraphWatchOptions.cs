namespace TaxVision.Connectors.Infrastructure.Providers.Watch;

/// <summary>Endpoint público del webhook de Graph (Fase 7, GraphNotificationWebhookController) + secreto compartido para validar el <c>clientState</c> de cada notificación entrante.</summary>
public sealed class GraphWatchOptions
{
    public const string SectionName = "Connectors:Watch:Graph";

    public string NotificationUrl { get; set; } = string.Empty;
    public string ClientState { get; set; } = string.Empty;
}
