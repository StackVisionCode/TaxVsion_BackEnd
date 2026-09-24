using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles;
using TaxVision.Signature.Application.Profiles.EffectiveSignature;

namespace TaxVision.Signature.Application.Requests.Commands.PreparerFields;

public static class SetPreparerSignatureHandler
{
    public static async Task<Result> Handle(
        SetPreparerSignatureCommand cmd,
        ISignatureRequestRepository repository,
        ISignatureProfileRepository profileRepository,
        ITenantSignatureSettingsRepository settingsRepository,
        IEffectiveSignatureResolver effectiveResolver,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        Guid fileId;
        if (cmd.SignatureFileId is { } explicitId)
        {
            // Solo se acepta el FileId de una firma que el actor puede ver (no un id arbitrario).
            // Con el toggle de firma propia apagado, un empleado no-admin no ve sus personales: se rechaza.
            var settings = await settingsRepository.GetByTenantIdAsync(cmd.TenantId, ct);
            var canUsePersonal = SignatureVisibilityPolicy.CanUsePersonal(cmd.ActorIsAdmin, settings);
            var visible = await profileRepository.ListVisibleAsync(
                cmd.TenantId,
                cmd.ActorUserId,
                canUsePersonal,
                includeArchived: false,
                ct
            );
            if (!visible.Any(p => p.FileId == explicitId))
                return Result.Failure(
                    new Error("Signature.Profile.NotVisible", "That signature is not available to you.")
                );
            fileId = explicitId;
        }
        else
        {
            // Sin elección explícita: la firma efectiva (personal si el tenant lo permite, o la de oficina).
            var effective = await effectiveResolver.ResolveAsync(cmd.TenantId, cmd.ActorUserId, ct);
            if (effective.IsFailure)
                return Result.Failure(effective.Error);
            fileId = effective.Value.FileId;
        }

        var result = request.SetPreparerSignature(fileId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
