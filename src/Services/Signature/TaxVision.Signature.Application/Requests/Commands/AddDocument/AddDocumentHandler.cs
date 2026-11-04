using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Documents.Verification;
using TaxVision.Signature.Application.Limits;

namespace TaxVision.Signature.Application.Requests.Commands.AddDocument;

public static class AddDocumentHandler
{
    public static async Task<Result<SignatureRequestDocumentResponse>> Handle(
        AddDocumentCommand cmd,
        ISignatureRequestRepository repository,
        IRequestDocumentVerifier verifier,
        ISignatureLimitsResolver limits,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<SignatureRequestDocumentResponse>(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var policy = await limits.ResolveAsync(cmd.TenantId, ct);
        var added = request.AddDocument(policy.ForRequests, cmd.OriginalFileId, cmd.Title, cmd.Note, cmd.PageCount);
        if (added.IsFailure)
            return Result.Failure<SignatureRequestDocumentResponse>(added.Error);

        // Si el archivo ya está disponible se revisa ahora; uno inválido no se agrega.
        var verification = await verifier.VerifyAsync(request, added.Value.Id, ct);
        if (verification.Kind == DocumentVerificationKind.Rejected)
            return Result.Failure<SignatureRequestDocumentResponse>(
                DocumentVerificationErrors.Rejected(added.Value, verification.Rejection!)
            );

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);

        return Result.Success(SignatureRequestDocumentResponse.From(added.Value));
    }
}
