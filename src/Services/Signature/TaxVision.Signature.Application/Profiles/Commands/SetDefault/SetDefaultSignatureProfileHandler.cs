using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles.Authorization;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Profiles.Commands.SetDefault;

public static class SetDefaultSignatureProfileHandler
{
    public static async Task<Result> Handle(
        SetDefaultSignatureProfileCommand cmd,
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

        var scope = await repository.ListByOwnerAsync(cmd.TenantId, profile.OwnerUserId, includeArchived: true, ct);
        var marked = DefaultSignatureProfilePolicy.SetDefault(scope, profile);
        if (marked.IsFailure)
            return marked;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
