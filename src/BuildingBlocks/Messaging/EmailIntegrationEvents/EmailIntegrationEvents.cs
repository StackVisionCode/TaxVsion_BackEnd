namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>
/// Solicitud de entrega de un correo saliente ya persistido (estado Queued). La publica el handler de
/// envío y la consume el propio servicio Notification para entregar de forma asíncrona y durable
/// (fuera del request HTTP). El cuerpo NO viaja en el evento: se lee de la BD por MessageId.
/// </summary>
public sealed record EmailSendRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
}

/// <summary>El correo se entregó al proveedor correctamente.</summary>
public sealed record EmailDeliverySucceededIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
    public required string ProviderType { get; init; }
    public Guid? CampaignId { get; init; }
}

/// <summary>El correo falló al entregarse (tras agotar reintentos o error no recuperable).</summary>
public sealed record EmailDeliveryFailedIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
    public required string Error { get; init; }
    public Guid? CampaignId { get; init; }
}

/// <summary>La campaña llegó a su hora programada y debe iniciar el fan-out de envíos (consumo interno).</summary>
public sealed record EmailCampaignStartedIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
}

/// <summary>Un lote de destinatarios de una campaña a procesar (fan-out por lotes, consumo interno).</summary>
public sealed record EmailCampaignBatchIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required int Skip { get; init; }
    public required int Take { get; init; }
}

/// <summary>La campaña quedó programada.</summary>
public sealed record EmailCampaignScheduledIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required DateTime ScheduledAtUtc { get; init; }
}

/// <summary>La campaña terminó (todos los destinatarios procesados).</summary>
public sealed record EmailCampaignCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required int SentCount { get; init; }
    public required int FailedCount { get; init; }
}

/// <summary>Se conectó una cuenta de correo externa (dispara la primera sincronización).</summary>
public sealed record EmailAccountConnectedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required string EmailAddress { get; init; }
}

/// <summary>Se desconectó una cuenta de correo externa.</summary>
public sealed record EmailAccountDisconnectedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
}

/// <summary>Solicitud de sincronización COMPLETA de una cuenta (consumo interno del worker).</summary>
public sealed record EmailFullSyncRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
}

/// <summary>Solicitud de sincronización INCREMENTAL de una cuenta (consumo interno del worker).</summary>
public sealed record EmailIncrementalSyncRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
}

/// <summary>La sincronización de una cuenta terminó con éxito.</summary>
public sealed record EmailSyncCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required int FoldersSynced { get; init; }
    public required int MessagesSynced { get; init; }
}

/// <summary>La sincronización de una cuenta falló.</summary>
public sealed record EmailSyncFailedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required string Error { get; init; }
}
