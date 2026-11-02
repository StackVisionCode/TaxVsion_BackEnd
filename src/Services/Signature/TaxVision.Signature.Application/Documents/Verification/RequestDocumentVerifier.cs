using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Application.Documents.Verification;

/// <summary>
/// Fases: (1) chequeos baratos con la metadata de CloudStorage (tipo y tamaño, sin descargar), (2) hechos del
/// PDF — reutilizados de un preflight del mismo contenido o, si no hay, descargando e inspeccionando, (3)
/// límites del tenant, (4) aplicar verificado/rechazado al aggregate.
/// </summary>
public sealed class RequestDocumentVerifier(
    IFileMetadataRefRepository files,
    IDocumentValidationRepository validations,
    ISignatureCloudStorageClient storage,
    IPdfInspector inspector,
    IDocumentLimitsResolver limitsResolver,
    ILogger<RequestDocumentVerifier> logger
) : IRequestDocumentVerifier
{
    public async Task<DocumentVerificationResult> VerifyAsync(
        SignatureRequest request,
        Guid documentId,
        CancellationToken ct
    )
    {
        var document = FindDocument(request, documentId);
        var file = await files.GetByFileIdAsync(request.TenantId, document.OriginalFileId, ct);
        if (file is not { Status: FileScanStatus.Available } || string.IsNullOrEmpty(file.ChecksumSha256))
            return DocumentVerificationResult.Deferred;

        return await VerifyAsync(
            request,
            documentId,
            new VerifiableFile(file.ChecksumSha256, file.ContentType, file.SizeBytes),
            ct
        );
    }

    public async Task<DocumentVerificationResult> VerifyAsync(
        SignatureRequest request,
        Guid documentId,
        VerifiableFile file,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(file);
        var document = FindDocument(request, documentId);

        var hash = DocumentHash.Create(file.ChecksumSha256);
        if (hash.IsFailure)
        {
            logger.LogWarning(
                "Document {DocumentId} of request {RequestId} has an invalid checksum; verification deferred.",
                documentId,
                request.Id
            );
            return DocumentVerificationResult.Deferred;
        }

        var limits = await limitsResolver.ResolveAsync(request.TenantId, ct);

        // (1) Sin descargar: un archivo que no es PDF o que ya excede el tope no vale el costo de bajarlo.
        if (!DocumentAcceptancePolicy.IsPdfContentType(file.ContentType))
            return Reject(request, document, DocumentAcceptancePolicy.NotAPdf());
        if (file.SizeBytes > limits.MaxPdfBytes)
            return Reject(request, document, DocumentAcceptancePolicy.SizeTooLarge(limits));

        // (2) Hechos del PDF.
        var facts = await ResolveFactsAsync(request, document, file, hash.Value, limits, ct);
        if (facts is null)
            return DocumentVerificationResult.Deferred;
        if (facts.StructuralIssue is not null)
            return Reject(request, document, facts.StructuralIssue);

        // (3) Límites del tenant.
        var issues = DocumentAcceptancePolicy.Evaluate(
            facts.SizeBytes,
            facts.PageCount,
            facts.HasExistingSignatures,
            limits
        );
        if (issues.Count > 0)
            return Reject(request, document, issues[0]);

        // (4) Verificado con el número de páginas que contó el servidor.
        var verified = VerifiedDocument.Create(hash.Value, facts.PageCount!.Value);
        if (verified.IsFailure)
            return Reject(request, document, new DocumentValidationIssue(verified.Error.Code, verified.Error.Message));

        var applied = request.AttachVerifiedDocument(document.Id, verified.Value);
        if (applied.IsFailure)
        {
            logger.LogWarning(
                "Document {DocumentId} of request {RequestId} could not be marked verified: {Error}",
                document.Id,
                request.Id,
                applied.Error.Message
            );
            return DocumentVerificationResult.Deferred;
        }
        return DocumentVerificationResult.Verified;
    }

    // ------------------------------------------------------------------
    // Hechos del PDF: del preflight del mismo contenido o de una inspección nueva
    // ------------------------------------------------------------------

    private sealed record PdfFacts(
        long SizeBytes,
        int? PageCount,
        bool HasExistingSignatures,
        DocumentValidationIssue? StructuralIssue
    );

    private async Task<PdfFacts?> ResolveFactsAsync(
        SignatureRequest request,
        RequestDocument document,
        VerifiableFile file,
        DocumentHash hash,
        DocumentLimits limits,
        CancellationToken ct
    )
    {
        // Mismo SHA-256 = mismo contenido: el preflight del navegador (o una verificación previa) ya lo abrió.
        var known = await validations.FindInspectedAsync(request.TenantId, hash.Value, ct);
        if (known?.PageCount is { } knownPages)
            return new PdfFacts(known.SizeBytes, knownPages, known.HasExistingSignatures, null);

        var download = await storage.DownloadAsync(request.TenantId, document.OriginalFileId, ct);
        if (download.IsFailure)
        {
            logger.LogWarning(
                "Document {DocumentId} of request {RequestId} could not be downloaded for verification: {Error}",
                document.Id,
                request.Id,
                download.Error.Message
            );
            return null;
        }

        var inspection = inspector.Inspect(download.Value, file.ContentType);
        if (!string.Equals(inspection.ContentSha256, hash.Value, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Document {DocumentId} of request {RequestId}: downloaded content does not match the CloudStorage checksum.",
                document.Id,
                request.Id
            );
            return null;
        }

        await RememberInspectionAsync(request, document, file, inspection, limits, ct);
        return new PdfFacts(
            inspection.SizeBytes,
            inspection.PageCount,
            inspection.HasExistingSignatures,
            inspection.StructuralIssues.Count > 0 ? inspection.StructuralIssues[0] : null
        );
    }

    // Queda como registro de validación (auditoría) y evita volver a descargar el mismo contenido.
    private async Task RememberInspectionAsync(
        SignatureRequest request,
        RequestDocument document,
        VerifiableFile file,
        PdfInspection inspection,
        DocumentLimits limits,
        CancellationToken ct
    )
    {
        var outcome = DocumentAcceptancePolicy.Combine(inspection, limits);
        var fileName = document.Title.Length > DocumentValidationRecord.MaxFileNameLength
            ? document.Title[..DocumentValidationRecord.MaxFileNameLength]
            : document.Title;
        var first = outcome.Issues.Count > 0 ? outcome.Issues[0] : null;

        var record = first is null
            ? DocumentValidationRecord.RecordAccepted(
                request.TenantId,
                request.CreatedByUserId,
                inspection.ContentSha256,
                fileName,
                file.ContentType,
                inspection.SizeBytes,
                inspection.PageCount ?? 0,
                inspection.HasExistingSignatures
            )
            : DocumentValidationRecord.RecordRejected(
                request.TenantId,
                request.CreatedByUserId,
                inspection.ContentSha256,
                fileName,
                file.ContentType,
                inspection.SizeBytes,
                inspection.PageCount,
                inspection.HasExistingSignatures,
                first.Code,
                first.Message
            );

        if (record.IsSuccess)
            await validations.AddAsync(record.Value, ct);
    }

    private DocumentVerificationResult Reject(
        SignatureRequest request,
        RequestDocument document,
        DocumentValidationIssue issue
    )
    {
        var rejection = DocumentRejection.Create(issue.Code, issue.Message);
        if (rejection.IsFailure || request.RejectDocument(document.Id, rejection.Value).IsFailure)
        {
            logger.LogWarning(
                "Document {DocumentId} of request {RequestId} failed verification ({Code}) but could not be marked rejected.",
                document.Id,
                request.Id,
                issue.Code
            );
            return DocumentVerificationResult.Deferred;
        }

        logger.LogInformation(
            "Document {DocumentId} of request {RequestId} rejected by server verification: {Code}.",
            document.Id,
            request.Id,
            issue.Code
        );
        return DocumentVerificationResult.Rejected(rejection.Value);
    }

    private static RequestDocument FindDocument(SignatureRequest request, Guid documentId) =>
        request.Documents.SingleOrDefault(document => document.Id == documentId)
        ?? throw new InvalidOperationException($"Document {documentId} does not belong to request {request.Id}.");
}
