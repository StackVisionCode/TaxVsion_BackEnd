using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles.Authorization;

namespace TaxVision.Signature.Application.Profiles.Commands.Rename;

public static class RenameSignatureProfileHandler
{
    public static async Task<Result> Handle(
        RenameSignatureProfileCommand cmd,
        ISignatureProfileRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var profile = await repository.GetByIdAsync(cmd.TenantId, cmd.ProfileId, ct);
        if (profile is null)
            return Result.Failure(SignatureProfileErrors.NotFound);

        var authorize = SignatureProfileAuthorization.EnsureCanManage(profile, cmd.ActorUserId, cmd.ActorIsAdmin);
        if (authorize.IsFailure)
            return authorize;

        var renamed = profile.Rename(cmd.Label);
        if (renamed.IsFailure)
            return renamed;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
