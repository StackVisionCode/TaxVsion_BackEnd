using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Validation;

/// <summary>
/// Registro immutable de un preflight de documento. Se guarda por auditoría — cada intento
/// de subir un PDF para firma queda con su timestamp, verdict, motivo y hash del contenido.
/// Nunca se muta: si el mismo archivo se re-valida, se crea otro registro.
/// </summary>
public sealed class DocumentValidationRecord : TenantEntity
{
    public const int MaxFileNameLength = 260;
    public const int MaxReasonLength = 500;

    private DocumentValidationRecord() { }

    public Guid RequestedByUserId { get; private set; }

    /// <summary>SHA-256 hex-lowercase del contenido validado. Permite dedup y trazabilidad.</summary>
    public string ContentSha256 { get; private set; } = default!;

    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public int? PageCount { get; private set; }
    public bool HasExistingSignatures { get; private set; }

    public DocumentValidationVerdict Verdict { get; private set; }
    public string? RejectionCode { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static Result<DocumentValidationRecord> RecordAccepted(
        Guid tenantId,
        Guid requestedByUserId,
        string contentSha256,
        string fileName,
        string contentType,
        long sizeBytes,
        int pageCount,
        bool hasExistingSignatures
    )
    {
        var baseValidation = ValidateFactoryInputs(tenantId, requestedByUserId, contentSha256, fileName, contentType);
        if (baseValidation.IsFailure)
            return Result.Failure<DocumentValidationRecord>(baseValidation.Error);

        var record = Build(
            tenantId,
            requestedByUserId,
            contentSha256,
            fileName,
            contentType,
            sizeBytes,
            pageCount,
            hasExistingSignatures,
            DocumentValidationVerdict.Accepted,
            null,
            null
        );
        return Result.Success(record);
    }

    public static Result<DocumentValidationRecord> RecordRejected(
        Guid tenantId,
        Guid requestedByUserId,
        string contentSha256,
        string fileName,
        string contentType,
        long sizeBytes,
        int? pageCount,
        bool hasExistingSignatures,
        string rejectionCode,
        string rejectionReason
    )
    {
        var baseValidation = ValidateFactoryInputs(tenantId, requestedByUserId, contentSha256, fileName, contentType);
        if (baseValidation.IsFailure)
            return Result.Failure<DocumentValidationRecord>(baseValidation.Error);

        if (string.IsNullOrWhiteSpace(rejectionCode))
            return Result.Failure<DocumentValidationRecord>(
                new Error("Signature.DocumentValidation.RejectionCode", "Rejection code is required.")
            );

        var record = Build(
            tenantId,
            requestedByUserId,
            contentSha256,
            fileName,
            contentType,
            sizeBytes,
            pageCount,
            hasExistingSignatures,
            DocumentValidationVerdict.Rejected,
            rejectionCode.Trim(),
            TruncateReason(rejectionReason)
        );
        return Result.Success(record);
    }

    // ------------------------------------------------------------------
    // Helpers privados — una responsabilidad por método
    // ------------------------------------------------------------------

    private static DocumentValidationRecord Build(
        Guid tenantId,
        Guid requestedByUserId,
        string contentSha256,
        string fileName,
        string contentType,
        long sizeBytes,
        int? pageCount,
        bool hasExistingSignatures,
        DocumentValidationVerdict verdict,
        string? rejectionCode,
        string? rejectionReason
    )
    {
        var record = new DocumentValidationRecord
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = requestedByUserId,
            ContentSha256 = contentSha256.ToLowerInvariant(),
            FileName = TruncateName(fileName),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
            PageCount = pageCount,
            HasExistingSignatures = hasExistingSignatures,
            Verdict = verdict,
            RejectionCode = rejectionCode,
            RejectionReason = rejectionReason,
            CreatedAtUtc = DateTime.UtcNow,
        };
        record.SetTenant(tenantId);
        return record;
    }

    private static Result ValidateFactoryInputs(
        Guid tenantId,
        Guid requestedByUserId,
        string contentSha256,
        string fileName,
        string contentType
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(new Error("Signature.DocumentValidation.Tenant", "TenantId is required."));
        if (requestedByUserId == Guid.Empty)
            return Result.Failure(
                new Error("Signature.DocumentValidation.RequestedBy", "RequestedByUserId is required.")
            );
        if (string.IsNullOrWhiteSpace(contentSha256) || contentSha256.Length != 64)
            return Result.Failure(
                new Error("Signature.DocumentValidation.Hash", "ContentSha256 must be a 64-char SHA-256.")
            );
        if (string.IsNullOrWhiteSpace(fileName))
            return Result.Failure(new Error("Signature.DocumentValidation.FileName", "FileName is required."));
        if (string.IsNullOrWhiteSpace(contentType))
            return Result.Failure(new Error("Signature.DocumentValidation.ContentType", "ContentType is required."));
        return Result.Success();
    }

    private static string TruncateName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length > MaxFileNameLength ? trimmed[..MaxFileNameLength] : trimmed;
    }

    private static string? TruncateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;
        var trimmed = reason.Trim();
        return trimmed.Length > MaxReasonLength ? trimmed[..MaxReasonLength] : trimmed;
    }
}
