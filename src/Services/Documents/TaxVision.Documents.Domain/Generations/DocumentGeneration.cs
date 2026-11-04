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

    /// <summary>P-18: segundo FileId del mismo PDF, destinado a Signature (owner/carpeta de firma). Nulo
    /// si la generación no pidió copia de firma. No se correlaciona con FileAvailable: su archivo lo lee
    /// Signature por M2M.</summary>
    public Guid? SignatureCopyFileId { get; private set; }

    /// <summary>P-18: página (1-indexada) donde la plantilla reservó la caja de firma. Nulo sin copia de firma.</summary>
    public int? SignatureAnchorPage { get; private set; }

    /// <summary>P-18: OwnerType/FolderType con que se subió la copia (el OwnerId es el de <see cref="Owner"/>).
    /// Hacen falta para pedir su borrado si la generación se cae; nulos en filas anteriores.</summary>
    public string? SignatureCopyOwnerType { get; private set; }
    public string? SignatureCopyFolderType { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Id externo de correlación (p.ej. el GenerationId que Tasks guarda en la revisión). El PK
    /// propio es independiente; esto deja que el emisor rote su id en los reintentos sin perder el vínculo.</summary>
    public Guid? ExternalReferenceId { get; private set; }
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
        DateTime nowUtc,
        Guid? externalReferenceId = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DocumentGeneration>(
                new Error("Documents.Generation.InvalidTenant", "TenantId is required.")
            );
        if (string.IsNullOrWhiteSpace(sourceService))
            return Result.Failure<DocumentGeneration>(
                new Error("Documents.Generation.InvalidSource", "SourceService is required.")
            );
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<DocumentGeneration>(
                new Error("Documents.Generation.InvalidIdempotencyKey", "IdempotencyKey is required.")
            );
        if (documentVersion < 1)
            return Result.Failure<DocumentGeneration>(
                new Error("Documents.Generation.InvalidDocumentVersion", "DocumentVersion must be >= 1.")
            );

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
            ExternalReferenceId = externalReferenceId,
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

    public Result Queue(DateTime nowUtc) =>
        Transition(DocumentGenerationStatus.Queued, nowUtc, DocumentGenerationStatus.Requested);

    public Result StartValidating(DateTime nowUtc) =>
        Transition(DocumentGenerationStatus.Validating, nowUtc, DocumentGenerationStatus.Queued);

    public Result StartRendering(DateTime nowUtc)
    {
        var r = Transition(
            DocumentGenerationStatus.Rendering,
            nowUtc,
            DocumentGenerationStatus.Validating,
            DocumentGenerationStatus.Queued
        );
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
            return Result.Failure(
                new Error("Documents.Generation.InvalidFileId", "FileId is required to start uploading.")
            );

        var r = Transition(
            DocumentGenerationStatus.Uploading,
            nowUtc,
            DocumentGenerationStatus.Rendering,
            DocumentGenerationStatus.Transforming,
            DocumentGenerationStatus.Packaging
        );
        if (r.IsSuccess)
            FileId = fileId;
        return r;
    }

    /// <summary>El archivo llegó a CloudStorage (evento FileAvailable). Congela la referencia de storage.</summary>
    public Result MarkStored(StorageReference storage, DateTime nowUtc)
    {
        if (FileId is not null && FileId != storage.FileId)
            return Result.Failure(
                new Error("Documents.Generation.FileIdMismatch", "Stored FileId does not match the uploaded FileId.")
            );

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

    /// <summary>CloudStorage rechazó el archivo que se estaba subiendo: ese FileId nunca existirá. Sólo
    /// aplica a la subida en curso (Uploading con ese mismo FileId); un reintento ya tiene otro FileId.</summary>
    public Result RejectUpload(Guid fileId, string errorCode, string errorMessage, DateTime nowUtc)
    {
        if (FileId is null || FileId != fileId)
            return Result.Failure(
                new Error("Documents.Generation.FileIdMismatch", "Rejected FileId does not match the uploaded FileId.")
            );
        if (Status != DocumentGenerationStatus.Uploading)
            return Result.Failure(
                new Error("Documents.Generation.InvalidTransition", $"Cannot reject the upload from {Status}.")
            );

        return Fail(errorCode, errorMessage, nowUtc);
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

    /// <summary>
    /// Desatasca una generación que arrancó y nunca terminó. Pasa cuando el paso siguiente se pierde fuera
    /// de acá —el archivo no llegó a almacenarse, el evento de vuelta no llegó— y la fila se queda a mitad
    /// de camino para siempre: sin esto, el único estado reintentable era <c>Failed</c>, y un atasco
    /// silencioso no lo es. Lo terminado (Completed, Cancelled) no se toca.
    /// </summary>
    public Result RetryStalled(DateTime nowUtc)
    {
        if (Status is DocumentGenerationStatus.Completed or DocumentGenerationStatus.Cancelled)
            return Result.Failure(new Error("Documents.Generation.InvalidTransition", $"Cannot retry from {Status}."));

        Status = DocumentGenerationStatus.Queued;
        ErrorCode = null;
        ErrorMessage = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>El emisor reintentó con un id de correlación nuevo (misma clave de idempotencia): se
    /// reapunta para que el evento de cierre vuelva a él, no al intento anterior.</summary>
    public void SetExternalReference(Guid externalReferenceId)
    {
        if (externalReferenceId != Guid.Empty)
            ExternalReferenceId = externalReferenceId;
    }

    public void SetContentHash(ContentHash hash, string fileName)
    {
        ContentHash = hash;
        FileName = fileName;
    }

    /// <summary>P-18: registra la copia del PDF para Signature (segundo FileId), la página del ancla de firma y
    /// el owner/carpeta con que se subió.</summary>
    public void AttachSignatureCopy(Guid signatureCopyFileId, int anchorPage, string ownerType, string folderType)
    {
        if (signatureCopyFileId != Guid.Empty)
            SignatureCopyFileId = signatureCopyFileId;
        if (anchorPage >= 1)
            SignatureAnchorPage = anchorPage;
        SignatureCopyOwnerType = string.IsNullOrWhiteSpace(ownerType) ? null : ownerType.Trim();
        SignatureCopyFolderType = string.IsNullOrWhiteSpace(folderType) ? null : folderType.Trim();
    }

    /// <summary>Suelta la copia de firma para que el cierre no anuncie un archivo inexistente o huérfano
    /// (CloudStorage la rechazó, o rechazó el archivo principal). Sólo antes de Completed (después ya se
    /// anunció) y sólo si sigue siendo esa copia.</summary>
    public Result DetachSignatureCopy(Guid signatureCopyFileId, DateTime nowUtc)
    {
        if (SignatureCopyFileId is null || SignatureCopyFileId != signatureCopyFileId)
            return Result.Failure(
                new Error("Documents.Generation.FileIdMismatch", "Signature copy FileId does not match.")
            );
        if (Status is DocumentGenerationStatus.Completed)
            return Result.Failure(
                new Error("Documents.Generation.InvalidTransition", "Signature copy was already announced.")
            );

        SignatureCopyFileId = null;
        SignatureAnchorPage = null;
        SignatureCopyOwnerType = null;
        SignatureCopyFolderType = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private Result Transition(
        DocumentGenerationStatus target,
        DateTime nowUtc,
        params DocumentGenerationStatus[] allowedFrom
    )
    {
        if (!allowedFrom.Contains(Status))
            return Result.Failure(
                new Error("Documents.Generation.InvalidTransition", $"Cannot move to {target} from {Status}.")
            );

        Status = target;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
