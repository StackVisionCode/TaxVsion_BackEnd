using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Application.Documents.Commands.Validate;

/// <summary>
/// Preflight de documento antes de subirlo como <c>OriginalFileId</c> de una
/// <c>SignatureRequest</c>. Fases explícitas por método privado:
/// <list type="number">
///   <item>Delegar en <see cref="IDocumentValidator"/> el análisis técnico.</item>
///   <item>Convertir el veredicto en <see cref="DocumentValidationRecord"/> (Accepted/Rejected).</item>
///   <item>Persistir el registro y mapear a la respuesta.</item>
/// </list>
/// </summary>
public static class ValidateDocumentHandler
{
    public static async Task<Result<ValidateDocumentResponse>> Handle(
        ValidateDocumentCommand cmd,
        IDocumentValidator validator,
        IDocumentValidationRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var basicChecks = ValidateInputs(cmd);
        if (basicChecks.IsFailure)
            return Result.Failure<ValidateDocumentResponse>(basicChecks.Error);

        var outcome = validator.Validate(cmd.Content, cmd.FileName, cmd.ContentType);
        var recordResult = BuildRecord(cmd, outcome);
        if (recordResult.IsFailure)
            return Result.Failure<ValidateDocumentResponse>(recordResult.Error);

        await repository.AddAsync(recordResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(MapToResponse(outcome, recordResult.Value.Id));
    }

    private static Result ValidateInputs(ValidateDocumentCommand cmd)
    {
        if (cmd.Content is null || cmd.Content.Length == 0)
            return Result.Failure(new Error("Signature.DocumentValidation.Empty", "Document content is required."));
        if (string.IsNullOrWhiteSpace(cmd.FileName))
            return Result.Failure(new Error("Signature.DocumentValidation.FileName", "FileName is required."));
        if (string.IsNullOrWhiteSpace(cmd.ContentType))
            return Result.Failure(new Error("Signature.DocumentValidation.ContentType", "ContentType is required."));
        return Result.Success();
    }

    private static Result<DocumentValidationRecord> BuildRecord(
        ValidateDocumentCommand cmd,
        DocumentValidationOutcome outcome
    )
    {
        if (outcome.IsAcceptable)
        {
            return DocumentValidationRecord.RecordAccepted(
                cmd.TenantId,
                cmd.RequestedByUserId,
                outcome.ContentSha256,
                cmd.FileName,
                cmd.ContentType,
                outcome.SizeBytes,
                outcome.PageCount ?? 0,
                outcome.HasExistingSignatures
            );
        }

        var first =
            outcome.Issues.Count > 0
                ? outcome.Issues[0]
                : new DocumentValidationIssue("Signature.DocumentValidation.Unknown", "Document rejected.");
        return DocumentValidationRecord.RecordRejected(
            cmd.TenantId,
            cmd.RequestedByUserId,
            outcome.ContentSha256,
            cmd.FileName,
            cmd.ContentType,
            outcome.SizeBytes,
            outcome.PageCount,
            outcome.HasExistingSignatures,
            first.Code,
            first.Message
        );
    }

    private static ValidateDocumentResponse MapToResponse(DocumentValidationOutcome outcome, Guid recordId) =>
        new(
            outcome.IsAcceptable,
            outcome.Issues.Select(i => new DocumentValidationIssueResponse(i.Code, i.Message)).ToList(),
            outcome.ContentSha256,
            outcome.SizeBytes,
            outcome.PageCount,
            outcome.HasExistingSignatures,
            recordId
        );
}
