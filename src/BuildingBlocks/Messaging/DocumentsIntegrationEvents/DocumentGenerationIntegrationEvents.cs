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

    /// <summary>Id externo con el que el servicio que pidió la generación la correlaciona (p.ej. Tasks
    /// guarda su propio GenerationId en la revisión). Nulo para generaciones sin correlación externa.</summary>
    public Guid? ExternalReferenceId { get; init; }
    public required string DocumentType { get; init; }
    public required string OwnerType { get; init; }
    public required Guid OwnerId { get; init; }
    public required int DocumentVersion { get; init; }
    public required Guid FileId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public string? ContentHash { get; init; }

    /// <summary>P-18: copias generadas (además de la principal). Aditivo; vacío si no hubo copia de firma.</summary>
    public IReadOnlyList<DocumentGenerationCopy> Copies { get; init; } = [];

    /// <summary>P-18: ancla de la caja de firma reservada en el PDF. Nulo si no se pidió copia de firma.</summary>
    public DocumentSignatureAnchor? SignatureAnchor { get; init; }
}

/// <summary>Una copia adicional del documento generado, con su propio FileId en CloudStorage (P-18).</summary>
public sealed record DocumentGenerationCopy
{
    public required string Purpose { get; init; }
    public required Guid FileId { get; init; }
    public string? ContentHash { get; init; }
}

/// <summary>Ancla de la caja de firma reservada en el PDF, normalizada 0..1 (P-18).</summary>
public sealed record DocumentSignatureAnchor
{
    public required int Page { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double W { get; init; }
    public required double H { get; init; }
}

/// <summary>La generación falló de forma terminal.</summary>
public sealed record DocumentGenerationFailedIntegrationEvent : DocumentsIntegrationEvent
{
    public override string EventType => "documents.generation.failed";
    public required Guid GenerationId { get; init; }

    /// <summary>Id externo con el que el servicio que pidió la generación la correlaciona (ver Completed).</summary>
    public Guid? ExternalReferenceId { get; init; }

    /// <summary>Tipo de documento, para que el servicio emisor filtre sus propias generaciones. Aditivo.</summary>
    public string? DocumentType { get; init; }
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
