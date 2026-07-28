using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Documents.Domain.ValueObjects;

namespace TaxVision.Documents.Domain.Generations;

/// <summary>
/// Aggregate root que representa UNA generación documental (no el archivo — ese es de CloudStorage).
/// Dueño de su máquina de estados técnica. No conoce EF, RabbitMQ, MinIO ni Playwright. Los eventos
/// de integración los publica la capa de aplicación (donde ya tiene el aggregate en mano y evita
/// recargarlo); el drenaje de domain events del DbContext queda listo para cuando haga falta.
///
/// Cada transición es un método explícito con su propia guarda (no hay un ChangeStatus genérico).
/// </summary>
public sealed class DocumentGeneration : AggregateRoot
{
    public DocumentType DocumentType { get; private set; } = null!;
    public TemplateKey TemplateKey { get; private set; } = null!;
    public int TemplateVersion { get; private set; }
    public DocumentOutputFormat OutputFormat { get; private set; }
    public GenerationOwner Owner { get; private set; } = null!;
    public string SourceService { get; private set; } = string.Empty;
    public int DocumentVersion { get; private set; }
    public DocumentPriority Priority { get; private set; }

    public DocumentGenerationStatus Status { get; private set; }

    /// <summary>FileId que Documents generó y está subiendo a CloudStorage. Se fija en StartUploading,
    /// ANTES de que llegue el evento FileAvailable — es la clave de correlación de ese evento con esta
    /// generación (el consumer corre cross-tenant y matchea por este FileId).</summary>
    public Guid? FileId { get; private set; }

    public StorageReference? Storage { get; private set; }
    public string? FileName { get; private set; }
    public ContentHash? ContentHash { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;
    public string? CausationId { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private DocumentGeneration() { }

    public static Result<DocumentGeneration> Request(
        Guid tenantId,
        DocumentType documentType,
        TemplateKey templateKey,
        int templateVersion,
        DocumentOutputFormat outputFormat,
        GenerationOwner owner,
        string sourceService,
        int documentVersion,
        DocumentPriority priority,
        string idempotencyKey,
        string correlationId,
        string? causationId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DocumentGeneration>(new Error("Documents.Generation.InvalidTenant", "TenantId is required."));
        if (string.IsNullOrWhiteSpace(sourceService))
            return Result.Failure<DocumentGeneration>(new Error("Documents.Generation.InvalidSource", "SourceService is required."));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<DocumentGeneration>(new Error("Documents.Generation.InvalidIdempotencyKey", "IdempotencyKey is required."));
        if (documentVersion < 1)
            return Result.Failure<DocumentGeneration>(new Error("Documents.Generation.InvalidDocumentVersion", "DocumentVersion must be >= 1."));

        var generation = new DocumentGeneration
        {
            DocumentType = documentType,
            TemplateKey = templateKey,
            TemplateVersion = templateVersion,
            OutputFormat = outputFormat,
            Owner = owner,
            SourceService = sourceService.Trim(),
            DocumentVersion = documentVersion,
            Priority = priority,
            Status = DocumentGenerationStatus.Requested,
            IdempotencyKey = idempotencyKey.Trim(),
            CorrelationId = correlationId ?? string.Empty,
            CausationId = causationId,
            RequestedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        generation.SetTenant(tenantId);
        return Result.Success(generation);
    }

    // --- Transiciones explícitas (guardrail #2). El avance normal es lineal; cada método valida
    //     su estado de origen permitido. ---

    public Result Queue(DateTime nowUtc) => Transition(DocumentGenerationStatus.Queued, nowUtc, DocumentGenerationStatus.Requested);

    public Result StartValidating(DateTime nowUtc) => Transition(DocumentGenerationStatus.Validating, nowUtc, DocumentGenerationStatus.Queued);

    public Result StartRendering(DateTime nowUtc)
    {
        var r = Transition(DocumentGenerationStatus.Rendering, nowUtc, DocumentGenerationStatus.Validating, DocumentGenerationStatus.Queued);
        if (r.IsSuccess)
        {
            StartedAtUtc ??= nowUtc;
            AttemptCount++;
        }
        return r;
    }

    /// <summary>Se subió el archivo al bucket temporal; se registra el FileId (correlación de FileAvailable).</summary>
    public Result StartUploading(Guid fileId, DateTime nowUtc)
    {
        if (fileId == Guid.Empty)
            return Result.Failure(new Error("Documents.Generation.InvalidFileId", "FileId is required to start uploading."));

        var r = Transition(DocumentGenerationStatus.Uploading, nowUtc, DocumentGenerationStatus.Rendering, DocumentGenerationStatus.Transforming, DocumentGenerationStatus.Packaging);
        if (r.IsSuccess)
            FileId = fileId;
        return r;
    }

    /// <summary>El archivo llegó a CloudStorage (evento FileAvailable). Congela la referencia de storage.</summary>
    public Result MarkStored(StorageReference storage, DateTime nowUtc)
    {
        if (FileId is not null && FileId != storage.FileId)
            return Result.Failure(new Error("Documents.Generation.FileIdMismatch", "Stored FileId does not match the uploaded FileId."));

        var r = Transition(DocumentGenerationStatus.Stored, nowUtc, DocumentGenerationStatus.Uploading);
        if (r.IsSuccess)
            Storage = storage;
        return r;
    }

    public Result Complete(DateTime nowUtc)
    {
        var r = Transition(DocumentGenerationStatus.Completed, nowUtc, DocumentGenerationStatus.Stored);
        if (r.IsFailure)
            return r;

        CompletedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Fail(string errorCode, string errorMessage, DateTime nowUtc)
    {
        if (Status is DocumentGenerationStatus.Completed or DocumentGenerationStatus.Cancelled)
            return Result.Failure(new Error("Documents.Generation.InvalidTransition", $"Cannot fail from {Status}."));

        Status = DocumentGenerationStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Cancel(DateTime nowUtc)
    {
        if (Status is DocumentGenerationStatus.Completed or DocumentGenerationStatus.Cancelled)
            return Result.Failure(new Error("Documents.Generation.InvalidTransition", $"Cannot cancel from {Status}."));

        Status = DocumentGenerationStatus.Cancelled;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Reintento controlado: una generación Failed vuelve a Queued para un nuevo intento
    /// (no salta directo a Rendering — guardrail: no reabrir estado sin control).</summary>
    public Result RetryFromFailure(DateTime nowUtc)
    {
        if (Status != DocumentGenerationStatus.Failed)
            return Result.Failure(new Error("Documents.Generation.InvalidTransition", $"Cannot retry from {Status}."));

        Status = DocumentGenerationStatus.Queued;
        ErrorCode = null;
        ErrorMessage = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void SetContentHash(ContentHash hash, string fileName)
    {
        ContentHash = hash;
        FileName = fileName;
    }

    private Result Transition(DocumentGenerationStatus target, DateTime nowUtc, params DocumentGenerationStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
            return Result.Failure(new Error("Documents.Generation.InvalidTransition", $"Cannot move to {target} from {Status}."));

        Status = target;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
