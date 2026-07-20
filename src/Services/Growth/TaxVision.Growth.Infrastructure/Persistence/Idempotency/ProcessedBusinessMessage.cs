using BuildingBlocks.Domain;

namespace TaxVision.Growth.Infrastructure.Persistence.Idempotency;

/// <summary>
/// Business-effect idempotency record. Wolverine's durable inbox deduplicates transport
/// envelopes; this record independently protects an operation, scope and idempotency key.
/// </summary>
public sealed class ProcessedBusinessMessage : TenantEntity
{
    public string Operation { get; private set; } = default!;
    public Guid ScopeId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string RequestFingerprint { get; private set; } = default!;
    public ProcessedBusinessMessageStatus Status { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? ResponseContentType { get; private set; }
    public string? ResponseJson { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ProcessedBusinessMessage() { }

    public static ProcessedBusinessMessage Begin(
        Guid tenantId,
        string operation,
        Guid scopeId,
        string idempotencyKey,
        string requestFingerprint,
        DateTime createdAtUtc,
        DateTime expiresAtUtc
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);

        if (operation.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(operation), "Operation cannot exceed 100 characters.");
        if (scopeId == Guid.Empty)
            throw new ArgumentException("ScopeId is required.", nameof(scopeId));
        if (idempotencyKey.Length > 200)
            throw new ArgumentOutOfRangeException(
                nameof(idempotencyKey),
                "Idempotency key cannot exceed 200 characters."
            );
        if (requestFingerprint.Length != 64 || !requestFingerprint.All(Uri.IsHexDigit))
            throw new ArgumentOutOfRangeException(
                nameof(requestFingerprint),
                "Request fingerprint must be a 64-character SHA-256 hexadecimal value."
            );
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("CreatedAtUtc must be UTC.", nameof(createdAtUtc));
        if (expiresAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("ExpiresAtUtc must be UTC and later than CreatedAtUtc.", nameof(expiresAtUtc));

        var message = new ProcessedBusinessMessage
        {
            Operation = operation,
            ScopeId = scopeId,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint.ToLowerInvariant(),
            Status = ProcessedBusinessMessageStatus.Processing,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
        };
        message.SetTenant(tenantId);
        return message;
    }

    public void Complete(
        int responseStatusCode,
        string? responseContentType,
        string? responseJson,
        DateTime completedAtUtc
    )
    {
        EnsureProcessing();
        EnsureCompletionTime(completedAtUtc);

        Status = ProcessedBusinessMessageStatus.Completed;
        ResponseStatusCode = responseStatusCode;
        ResponseContentType = responseContentType;
        ResponseJson = responseJson;
        CompletedAtUtc = completedAtUtc;
    }

    public void Fail(string failureCode, DateTime completedAtUtc)
    {
        EnsureProcessing();
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        EnsureCompletionTime(completedAtUtc);

        if (failureCode.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(failureCode), "Failure code cannot exceed 100 characters.");

        Status = ProcessedBusinessMessageStatus.Failed;
        FailureCode = failureCode;
        CompletedAtUtc = completedAtUtc;
    }

    public bool HasSameFingerprint(string requestFingerprint) =>
        string.Equals(RequestFingerprint, requestFingerprint, StringComparison.OrdinalIgnoreCase);

    private void EnsureProcessing()
    {
        if (Status != ProcessedBusinessMessageStatus.Processing)
            throw new InvalidOperationException($"Business message is already {Status}.");
    }

    private void EnsureCompletionTime(DateTime completedAtUtc)
    {
        if (completedAtUtc.Kind != DateTimeKind.Utc || completedAtUtc < CreatedAtUtc)
            throw new ArgumentException(
                "CompletedAtUtc must be UTC and not precede CreatedAtUtc.",
                nameof(completedAtUtc)
            );
    }
}
