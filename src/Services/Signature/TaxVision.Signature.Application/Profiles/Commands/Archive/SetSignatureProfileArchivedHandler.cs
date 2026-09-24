using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles.Authorization;

namespace TaxVision.Signature.Application.Profiles.Commands.Archive;

public static class SetSignatureProfileArchivedHandler
{
    public static async Task<Result> Handle(
        SetSignatureProfileArchivedCommand cmd,
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

        var result = cmd.Archived ? profile.Archive() : profile.Unarchive();
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
