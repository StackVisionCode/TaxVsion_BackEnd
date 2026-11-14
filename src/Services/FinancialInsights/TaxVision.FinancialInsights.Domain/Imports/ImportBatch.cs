using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.FinancialInsights.Domain.Imports.Events;

namespace TaxVision.FinancialInsights.Domain.Imports;

/// <summary>
/// Lote de estados de cuenta de un cliente: raíz del agregado. Registra de 1 a 20 archivos distintos ya subidos a
/// CloudStorage y es el único camino para cambiar el estado de sus documentos.
/// </summary>
public sealed class ImportBatch : AggregateRoot, ITenantOwned
{
    public const int MaxDocuments = 20;

    private readonly List<ImportDocument> _documents = [];

    private ImportBatch() { }

    public Guid CustomerId { get; private set; }

    // Quién subió el lote: procedencia, el retiro no la toca.
    [UserReference(UserReferenceKind.Provenance)]
    public Guid CreatedBy { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public ImportBatchSource Source { get; private set; }

    /// <summary>Cuándo se avisó el cierre del ciclo actual. Evita un segundo aviso; un reintento lo limpia.</summary>
    public DateTime? CompletionNotifiedAtUtc { get; private set; }

    public IReadOnlyList<ImportDocument> Documents => _documents;

    public static Result<ImportBatch> Create(
        Guid tenantId,
        Guid customerId,
        Guid createdBy,
        IReadOnlyCollection<Guid>? fileIds,
        ImportBatchSource source = ImportBatchSource.Staff
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ImportBatch>(FinancialInsightsErrors.TenantRequired);
        if (customerId == Guid.Empty)
            return Result.Failure<ImportBatch>(FinancialInsightsErrors.CustomerRequired);
        if (createdBy == Guid.Empty)
            return Result.Failure<ImportBatch>(FinancialInsightsErrors.CreatorRequired);
        if (
            fileIds is null
            || fileIds.Count is < 1 or > MaxDocuments
            || fileIds.Any(id => id == Guid.Empty)
            || fileIds.Distinct().Count() != fileIds.Count
        )
            return Result.Failure<ImportBatch>(FinancialInsightsErrors.InvalidBatch);

        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            CreatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow,
            Source = source,
        };
        batch.SetTenant(tenantId);
        batch._documents.AddRange(fileIds.Select(fileId => ImportDocument.Create(batch.Id, tenantId, fileId)));
        return Result.Success(batch);
    }

    public new Result SetTenant(Guid tenantId)
    {
        var allowed = TenantGuard.CanAssign(TenantId, tenantId);
        if (allowed.IsSuccess)
            base.SetTenant(tenantId);
        return allowed;
    }

    void ITenantOwned.SetTenant(Guid tenantId) => SetTenant(tenantId);

    public ImportDocument? FindDocument(Guid documentId) => _documents.FirstOrDefault(d => d.Id == documentId);

    /// <summary>CloudStorage terminó el escaneo sin hallazgos.</summary>
    public Result MarkFileReady(Guid fileId) => OnFile(fileId, d => d.MarkReady());

    /// <summary>CloudStorage marcó el archivo como infectado, fallido o no elegible.</summary>
    public Result RejectFile(Guid fileId, string reason) => Finishing(OnFile(fileId, d => d.Reject(reason)));

    public Result MarkDocumentReady(Guid documentId) => OnDocument(documentId, d => d.MarkReady());

    public Result StartProcessing(Guid documentId) => OnDocument(documentId, d => d.StartProcessing());

    /// <summary>El worker lo devuelve a la cola sin consumir cupo (p. ej. el plan aún no llegó).</summary>
    public Result ReturnDocumentToQueue(Guid documentId) => OnDocument(documentId, d => d.ReturnToQueue());

    public Result CompleteDocument(Guid documentId, bool needsReview) =>
        Finishing(OnDocument(documentId, d => d.Complete(needsReview)));

    public Result FailDocument(Guid documentId, string reason) => Finishing(OnDocument(documentId, d => d.Fail(reason)));

    public Result HoldDocumentForReview(Guid documentId, string reason) =>
        Finishing(OnDocument(documentId, d => d.HoldForReview(reason)));

    /// <summary>Un reintento manual abre un ciclo nuevo: cuando vuelva a cerrar se avisa otra vez.</summary>
    public Result RetryDocument(Guid documentId, bool inferenceAttempted)
    {
        var retried = OnDocument(documentId, d => d.RetryBeforeInference(inferenceAttempted));
        if (retried.IsSuccess)
            CompletionNotifiedAtUtc = null;
        return retried;
    }

    // Tras un paso a estado final: si era el último documento y el ciclo no se avisó, se levanta el evento.
    private Result Finishing(Result transition)
    {
        if (transition.IsFailure || CompletionNotifiedAtUtc is not null || !_documents.All(d => IsFinal(d.Status)))
            return transition;

        var now = DateTime.UtcNow;
        CompletionNotifiedAtUtc = now;
        AddDomainEvent(
            new ImportBatchCompletedDomainEvent(
                Id,
                TenantId,
                CustomerId,
                CreatedBy,
                Source,
                Count(ImportDocumentStatus.Completed),
                Count(ImportDocumentStatus.NeedsReview),
                Count(ImportDocumentStatus.Failed),
                Count(ImportDocumentStatus.Rejected),
                now
            )
        );
        return transition;
    }

    private int Count(ImportDocumentStatus status) => _documents.Count(d => d.Status == status);

    private static bool IsFinal(ImportDocumentStatus status) =>
        status
            is ImportDocumentStatus.Completed
                or ImportDocumentStatus.NeedsReview
                or ImportDocumentStatus.Failed
                or ImportDocumentStatus.Rejected;

    private Result OnDocument(Guid documentId, Func<ImportDocument, Result> change) =>
        FindDocument(documentId) is { } document
            ? change(document)
            : Result.Failure(FinancialInsightsErrors.DocumentNotFound);

    private Result OnFile(Guid fileId, Func<ImportDocument, Result> change) =>
        _documents.FirstOrDefault(d => d.FileId == fileId) is { } document
            ? change(document)
            : Result.Failure(FinancialInsightsErrors.DocumentNotFound);
}
