using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Imports;

/// <summary>
/// Aggregate root que rastrea un intento de import masivo de customers.
/// Nombre dictado por la guia PDF del senior (pagina 8, sec 4.4).
///
/// Es un aggregate operacional: vive aparte del Customer aggregate, no se referencian por navegacion.
/// Customer publica CustomersBulkImportedV1 cuando este aggregate completa.
///
/// DDD: las mutaciones de child rows pasan SIEMPRE por metodos del aggregate
/// (RecordSuccess/RecordFailed/RecordSkipped/RecordUpdated). El handler NUNCA toca
/// CustomerImportRow directamente; el aggregate construye el row, lo marca e incrementa contadores
/// en una sola operacion atomica.
/// </summary>
public sealed class CustomerImportAttempt : TenantEntity
{
    private readonly List<CustomerImportRow> _rows = [];

    private CustomerImportAttempt() { }

    public Guid CreatedByUserId { get; private set; }

    /// <summary>Key unica por tenant para idempotencia HTTP estilo Stripe.</summary>
    public string IdempotencyKey { get; private set; } = default!;

    public ImportStatus Status { get; private set; }
    public DuplicateStrategy Strategy { get; private set; }
    public ImportSourceKind SourceKind { get; private set; }

    /// <summary>Nombre del archivo original (solo informativo para el reporte).</summary>
    public string SourceFileName { get; private set; } = default!;

    public int TotalRows { get; private set; }
    public int ProcessedRows { get; private set; }
    public int SuccessCount { get; private set; }
    public int UpdatedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int FailedCount { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CanceledAtUtc { get; private set; }
    public Guid? CanceledByUserId { get; private set; }

    /// <summary>Mensaje de fallo a nivel job (no por fila). Sin PII.</summary>
    public string? FailureReason { get; private set; }

    public IReadOnlyCollection<CustomerImportRow> Rows => _rows.AsReadOnly();

    public static Result<CustomerImportAttempt> Create(
        Guid tenantId,
        Guid createdByUserId,
        string idempotencyKey,
        DuplicateStrategy strategy,
        ImportSourceKind sourceKind,
        string sourceFileName
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CustomerImportAttempt>(new Error("Import.Tenant", "Tenant is required."));
        if (createdByUserId == Guid.Empty)
            return Result.Failure<CustomerImportAttempt>(new Error("Import.User", "CreatedByUserId is required."));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 80)
            return Result.Failure<CustomerImportAttempt>(
                new Error("Import.IdempotencyKey", "Idempotency key is required (max 80 chars).")
            );
        if (string.IsNullOrWhiteSpace(sourceFileName) || sourceFileName.Length > 256)
            return Result.Failure<CustomerImportAttempt>(
                new Error("Import.SourceFileName", "Source file name is required (max 256 chars).")
            );

        var attempt = new CustomerImportAttempt
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = createdByUserId,
            IdempotencyKey = idempotencyKey.Trim(),
            Status = ImportStatus.Queued,
            Strategy = strategy,
            SourceKind = sourceKind,
            SourceFileName = sourceFileName.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        attempt.SetTenant(tenantId);
        return Result.Success(attempt);
    }

    public Result Start()
    {
        if (Status != ImportStatus.Queued)
            return Result.Failure(new Error("Import.InvalidState", $"Cannot start an import in state {Status}."));

        Status = ImportStatus.Validating;
        StartedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MoveToApplying()
    {
        if (Status != ImportStatus.Validating)
            return Result.Failure(new Error("Import.InvalidState", $"Cannot move to Applying from {Status}."));

        Status = ImportStatus.Applying;
        return Result.Success();
    }

    public Result SetTotalRows(int totalRows)
    {
        if (totalRows < 0)
            return Result.Failure(new Error("Import.TotalRows", "TotalRows cannot be negative."));
        TotalRows = totalRows;
        return Result.Success();
    }

    // ============== Row recording (DDD: solo el aggregate muta sus rows) ==============

    /// <summary>Registra una fila exitosa (customer creado). Crea el row, lo marca, incrementa contadores.</summary>
    public void RecordSuccess(int rowNumber, Guid customerId, string displayName)
    {
        var row = AddOrGetRow(rowNumber);
        row.MarkSuccess(customerId, displayName);
        ProcessedRows++;
        SuccessCount++;
    }

    /// <summary>Registra una fila actualizada (duplicado matcheado con strategy Merge/Overwrite).</summary>
    public void RecordUpdated(int rowNumber, Guid existingCustomerId, string displayName, string matchedBy)
    {
        var row = AddOrGetRow(rowNumber);
        row.MarkUpdated(existingCustomerId, displayName, matchedBy);
        ProcessedRows++;
        UpdatedCount++;
    }

    /// <summary>Registra una fila saltada (duplicado matcheado con strategy Skip).</summary>
    public void RecordSkipped(int rowNumber, Guid existingCustomerId, string displayName, string matchedBy)
    {
        var row = AddOrGetRow(rowNumber);
        row.MarkSkipped(existingCustomerId, displayName, matchedBy);
        ProcessedRows++;
        SkippedCount++;
    }

    /// <summary>Registra una fila fallida (validacion, catalogo, dedup intra-chunk, etc).</summary>
    public void RecordFailed(int rowNumber, string? displayName, string errorCode, string message)
    {
        var row = AddOrGetRow(rowNumber);
        row.MarkFailed(displayName, errorCode, message);
        ProcessedRows++;
        FailedCount++;
    }

    private CustomerImportRow AddOrGetRow(int rowNumber)
    {
        var existing = _rows.FirstOrDefault(r => r.RowNumber == rowNumber);
        if (existing is not null)
            return existing;

        var row = CustomerImportRow.CreatePending(TenantId, Id, rowNumber);
        _rows.Add(row);
        return row;
    }

    // ============== Terminal transitions ==============

    public Result Complete()
    {
        if (Status is not (ImportStatus.Applying or ImportStatus.Validating))
            return Result.Failure(new Error("Import.InvalidState", $"Cannot complete an import in state {Status}."));

        Status = ImportStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Fail(string reason)
    {
        Status = ImportStatus.Failed;
        CompletedAtUtc = DateTime.UtcNow;
        FailureReason = string.IsNullOrWhiteSpace(reason)
            ? "Unknown error"
            : (reason.Length > 500 ? reason[..500] : reason);
        return Result.Success();
    }

    public Result RequestCancel(Guid byUserId)
    {
        if (Status is ImportStatus.Completed or ImportStatus.Failed or ImportStatus.Canceled)
            return Result.Failure(
                new Error("Import.AlreadyTerminal", $"Import is already in terminal state {Status}.")
            );
        if (Status == ImportStatus.Canceling)
            return Result.Success(); // idempotent

        Status = ImportStatus.Canceling;
        CanceledByUserId = byUserId;
        return Result.Success();
    }

    /// <summary>Confirma la cancelacion una vez que el worker observo el flag y dejo de procesar.</summary>
    public Result ConfirmCanceled()
    {
        if (Status != ImportStatus.Canceling)
            return Result.Failure(new Error("Import.NotCanceling", $"Cannot confirm cancel from {Status}."));

        Status = ImportStatus.Canceled;
        CanceledAtUtc = DateTime.UtcNow;
        CompletedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public bool IsCancelRequested => Status == ImportStatus.Canceling;

    public bool IsTerminal => Status is ImportStatus.Completed or ImportStatus.Failed or ImportStatus.Canceled;
}
