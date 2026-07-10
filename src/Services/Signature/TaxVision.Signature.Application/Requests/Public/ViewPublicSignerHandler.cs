using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Public;

public static class ViewPublicSignerHandler
{
    public static async Task<Result<PublicSignerView>> Handle(
        ViewPublicSignerCommand cmd,
        ISigningTokenService tokenService,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(cmd.Token, tokenService, repository, ct);
        if (resolution.IsFailure)
            return Result.Failure<PublicSignerView>(resolution.Error);

        var (request, signer) = (resolution.Value.Request, resolution.Value.Signer);
        RecordFirstView(request, signer, cmd);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(BuildView(request, signer));
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static void RecordFirstView(SignatureRequest request, Signer signer, ViewPublicSignerCommand cmd)
    {
        var viewedAt = DateTime.UtcNow;
        request.RecordSignerFirstView(signer.Id, viewedAt, cmd.ClientIp, cmd.UserAgent);
    }

    private static PublicSignerView BuildView(SignatureRequest request, Signer signer) =>
        new(
            SignatureRequestId: request.Id,
            SignerId: signer.Id,
            Title: request.Title,
            Description: request.Description,
            Category: request.Category,
            RequestStatus: request.Status,
            SignerStatus: signer.Status,
            OriginalFileId: request.OriginalFileId,
            RequiresConsent: request.RequiresConsent,
            HasAcceptedConsent: signer.HasAcceptedConsent,
            RequiresSequentialSigning: request.RequiresSequentialSigning,
            IsSignerNextInSequence: IsNextInSequence(request, signer),
            Order: signer.Order,
            ExpiresAtUtc: request.ExpiresAtUtc,
            SignerFullName: signer.FullName.Value,
            SignerEmail: signer.Email.Value,
            RequiresPractitionerPin: request.RequiresPractitionerPin,
            IsPinVerified: signer.IsPinVerified,
            PinLockedUntilUtc: signer.PinLockedUntilUtc,
            Fields: signer.Fields.Select(MapField).ToList()
        );

    private static bool IsNextInSequence(SignatureRequest request, Signer signer)
    {
        if (!request.RequiresSequentialSigning)
            return true;
        var next = request
            .Signers.Where(s => s.Status == Domain.Requests.SignerStatus.Pending)
            .OrderBy(s => s.Order)
            .FirstOrDefault();
        return next?.Id == signer.Id;
    }

    private static PublicSignerFieldView MapField(SignatureField field) =>
        new(
            field.Id,
            field.Kind,
            field.Position.Page,
            field.Position.X,
            field.Position.Y,
            field.Position.Width,
            field.Position.Height,
            field.Label,
            field.IsRequired
        );
}
