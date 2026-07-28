namespace BuildingBlocks.Messaging.DocumentsIntegrationEvents;

/// <summary>Un servicio pide una generación de forma totalmente asíncrona (sin M2M). Documents lo
/// consume. Idempotente por la clave del payload. Transporta solo referencias/datos, nunca bytes.</summary>
public sealed record DocumentGenerationRequestedIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.generation.requested";
    public required Guid GenerationId { get; init; }
    public required string DocumentType { get; init; }
    public required string TemplateKey { get; init; }
    public required int TemplateVersion { get; init; }
    public required string OutputFormat { get; init; }
    public required string OwnerType { get; init; }
    public required Guid OwnerId { get; init; }
    public required int DocumentVersion { get; init; }
    public required string SourceService { get; init; }
    public int? TaxYear { get; init; }
    public required string IdempotencyKey { get; init; }
}

/// <summary>Documents empezó a procesar una generación.</summary>
public sealed record DocumentGenerationStartedIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.generation.started";
    public required Guid GenerationId { get; init; }
    public required string DocumentType { get; init; }
}

/// <summary>La generación terminó y el archivo quedó en CloudStorage. El consumidor (p.ej. Billing)
/// reacciona con el FileId — nunca recibe bytes.</summary>
public sealed record DocumentGenerationCompletedIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.generation.completed";
    public required Guid GenerationId { get; init; }
    public required string DocumentType { get; init; }
    public required string OwnerType { get; init; }
    public required Guid OwnerId { get; init; }
    public required int DocumentVersion { get; init; }
    public required Guid FileId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public string? ContentHash { get; init; }
}

/// <summary>La generación falló de forma terminal.</summary>
public sealed record DocumentGenerationFailedIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.generation.failed";
    public required Guid GenerationId { get; init; }
    public required string OwnerType { get; init; }
    public required Guid OwnerId { get; init; }
    public required string ErrorCode { get; init; }
}

/// <summary>El archivo generado quedó almacenado (correlación con CloudStorage FileAvailable).</summary>
public sealed record DocumentStoredIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.stored";
    public required Guid GenerationId { get; init; }
    public required Guid FileId { get; init; }
    public required long SizeBytes { get; init; }
}
