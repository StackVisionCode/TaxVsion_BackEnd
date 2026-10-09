using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Documento que forma parte de una solicitud de firma. Su ciclo de vida se controla
/// exclusivamente desde <see cref="SignatureRequest"/>.
/// </summary>
public sealed class RequestDocument : BaseEntity
{
    public const int MaxTitleLength = 200;
    public const int MaxNoteLength = 500;

    private RequestDocument() { }

    public Guid SignatureRequestId { get; private set; }
    public int Order { get; private set; }
    public string Title { get; private set; } = default!;
    public Guid OriginalFileId { get; private set; }
    public DocumentHash? DocumentHashPre { get; private set; }
    public Guid? SealedFileId { get; private set; }
    public Guid? CertificateFileId { get; private set; }
    public DocumentHash? DocumentHashPost { get; private set; }
    public DateTime? SealedAtUtc { get; private set; }
    public string? Note { get; private set; }

    /// <summary>
    /// F9 — número de páginas del PDF cargado. Se usa al reemplazar el documento para decidir si
    /// las coordenadas normalizadas de los campos siguen siendo válidas. `null` para registros
    /// anteriores a F9 que no reportaron el conteo.
    /// </summary>
    public int? PageCount { get; private set; }

    internal static Result<RequestDocument> Create(
        Guid tenantId,
        Guid requestId,
        int order,
        string title,
        Guid originalFileId,
        string? note,
        int? pageCount = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<RequestDocument>(new Error("Signature.Document.Tenant", "TenantId is required."));
        if (requestId == Guid.Empty)
            return Result.Failure<RequestDocument>(
                new Error("Signature.Document.Request", "SignatureRequestId is required.")
            );
        if (originalFileId == Guid.Empty)
            return Result.Failure<RequestDocument>(
                new Error("Signature.Document.OriginalFile", "OriginalFileId is required.")
            );

        var titleResult = NormalizeTitle(title);
        if (titleResult.IsFailure)
            return Result.Failure<RequestDocument>(titleResult.Error);
        if (order < 1)
            return Result.Failure<RequestDocument>(
                new Error("Signature.Document.Order", "Document order must be greater than or equal to 1.")
            );

        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalizedNote is { Length: > MaxNoteLength })
            return Result.Failure<RequestDocument>(
                new Error("Signature.Document.Note", $"Document note cannot exceed {MaxNoteLength} characters.")
            );

        if (pageCount is <= 0)
            return Result.Failure<RequestDocument>(
                new Error("Signature.Document.PageCount", "PageCount must be greater than zero when provided.")
            );

        return Result.Success(
            new RequestDocument
            {
                Id = Guid.NewGuid(),
                SignatureRequestId = requestId,
                Order = order,
                Title = titleResult.Value,
                OriginalFileId = originalFileId,
                Note = normalizedNote,
                PageCount = pageCount,
            }
        );
    }

    internal Result AttachHashPre(DocumentHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        DocumentHashPre = hash;
        return Result.Success();
    }

    internal Result MarkSealed(Guid sealedFileId, DocumentHash hashPost, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(hashPost);
        if (sealedFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.Document.SealedFile", "SealedFileId is required."));

        if (SealedFileId == sealedFileId && DocumentHashPost == hashPost)
            return Result.Success();

        SealedFileId = sealedFileId;
        DocumentHashPost = hashPost;
        SealedAtUtc = nowUtc;
        return Result.Success();
    }

    internal Result RecordCertificate(Guid certificateFileId)
    {
        if (certificateFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.Document.CertificateFile", "CertificateFileId is required."));

        if (SealedFileId is null || DocumentHashPost is null || SealedAtUtc is null)
            return Result.Failure(
                new Error("Signature.Document.NotSealed", "A document certificate requires a sealed document.")
            );

        CertificateFileId = certificateFileId;
        return Result.Success();
    }

    internal Result ReplaceOriginalFile(Guid newFileId, int? newPageCount)
    {
        if (newFileId == Guid.Empty)
            return Result.Failure(new Error("Signature.Document.OriginalFile", "OriginalFileId is required."));
        if (newPageCount is <= 0)
            return Result.Failure(
                new Error("Signature.Document.PageCount", "PageCount must be greater than zero when provided.")
            );

        OriginalFileId = newFileId;
        PageCount = newPageCount;
        DocumentHashPre = null;
        SealedFileId = null;
        CertificateFileId = null;
        DocumentHashPost = null;
        SealedAtUtc = null;
        return Result.Success();
    }

    internal Result Rename(string newTitle)
    {
        var result = NormalizeTitle(newTitle);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        Title = result.Value;
        return Result.Success();
    }

    internal Result Reorder(int newOrder)
    {
        if (newOrder < 1)
            return Result.Failure(
                new Error("Signature.Document.Order", "Document order must be greater than or equal to 1.")
            );

        Order = newOrder;
        return Result.Success();
    }

    private static Result<string> NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure<string>(new Error("Signature.Document.Title", "Document title is required."));

        var normalized = title.Trim();
        return normalized.Length <= MaxTitleLength
            ? Result.Success(normalized)
            : Result.Failure<string>(
                new Error("Signature.Document.Title", $"Document title cannot exceed {MaxTitleLength} characters.")
            );
    }
}
