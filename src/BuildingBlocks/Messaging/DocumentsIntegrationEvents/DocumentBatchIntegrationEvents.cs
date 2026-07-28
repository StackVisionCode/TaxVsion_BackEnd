namespace BuildingBlocks.Messaging.DocumentsIntegrationEvents;

/// <summary>Solicitud de generación por lote.</summary>
public sealed record DocumentBatchRequestedIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.batch.requested";
    public required Guid BatchId { get; init; }
    public required string SourceService { get; init; }
    public required string PackageMode { get; init; }
    public required int ItemCount { get; init; }
}

/// <summary>Lote completado (todos los ítems OK).</summary>
public sealed record DocumentBatchCompletedIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.batch.completed";
    public required Guid BatchId { get; init; }
    public required int CompletedItems { get; init; }
    public Guid? PackageFileId { get; init; }
}

/// <summary>Lote parcialmente completado (algunos ítems fallaron). Documents publica completados +
/// fallidos; la decisión de qué hacer con los completados es del servicio dueño (p.ej. Billing).</summary>
public sealed record DocumentBatchPartiallyCompletedIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.batch.partially_completed";
    public required Guid BatchId { get; init; }
    public required int CompletedItems { get; init; }
    public required int FailedItems { get; init; }
}
