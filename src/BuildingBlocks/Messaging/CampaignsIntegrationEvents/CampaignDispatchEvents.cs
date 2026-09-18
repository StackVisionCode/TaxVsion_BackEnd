namespace BuildingBlocks.Messaging.CampaignsIntegrationEvents;

// ---------------------------------------------------------------------------
// Contrato dispatch/result común a todos los canales (documents/architecture/campaigns/
// campaigns/Commands_And_Events.md §2). Campaigns es un orquestador agnóstico que NO envía:
// publica CampaignDispatchRequested por unidad destinatario/canal y aplica CampaignDispatchResult.
// Correlación opaca por DispatchId (por intento) — el ejecutor la devuelve intacta. SIN dinero.
// ---------------------------------------------------------------------------

/// <summary><c>campaign.dispatch.requested.v1</c> — pedido de envío de UNA unidad (destinatario/canal).</summary>
public sealed record CampaignDispatchRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required Guid RunId { get; init; }
    public required Guid RecipientId { get; init; }
    public required int AttemptNo { get; init; }
    public required string DispatchId { get; init; }
    public required string Channel { get; init; } // Email | Sms | WhatsApp | Push | InApp
    public required string ContactRef { get; init; }
    public string? Email { get; init; }
    public string? PhoneE164 { get; init; }
    public string? SenderRef { get; init; }
    public string? ContentRef { get; init; }
    // Contenido resuelto/congelado del run (slice 3: inline; el ContentRef inmutable + render Scribe es fase posterior).
    public string? Subject { get; init; }
    public string? Body { get; init; }
}

/// <summary><c>campaign.dispatch.result.v1</c> — resultado reportado por el ejecutor de canal.</summary>
public sealed record CampaignDispatchResultIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required Guid RunId { get; init; }
    public required string DispatchId { get; init; }
    public required string Outcome { get; init; } // Accepted | Delivered | Failed | Skipped | Unknown
    public string? ProviderRef { get; init; }
    public string? Reason { get; init; }
}

/// <summary><c>campaign.run.started.v1</c> — publicado al arrancar una ejecución.</summary>
public sealed record CampaignRunStartedIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required Guid RunId { get; init; }
    public required int RecipientCount { get; init; }
    public required string TriggeredBy { get; init; }
}

/// <summary><c>campaign.run.completed.v1</c> — publicado al cerrar una ejecución (alimenta reporting y consumidores externos).</summary>
public sealed record CampaignRunCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required Guid RunId { get; init; }
    public required string TerminalStatus { get; init; }
    public required int RecipientCount { get; init; }
    public required int Delivered { get; init; }
    public required int Accepted { get; init; }
    public required int Failed { get; init; }
    public required int Skipped { get; init; }
    public required int Unknown { get; init; }
}
