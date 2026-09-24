using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles.Authorization;

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

        // Desmarca la default actual del MISMO ámbito antes de marcar la nueva (una sola por ámbito).
        var scope = await repository.ListByOwnerAsync(cmd.TenantId, profile.OwnerUserId, includeArchived: true, ct);
        foreach (var other in scope)
        {
            if (other.Id != profile.Id)
                other.UnsetDefault();
        }

        var marked = profile.MarkDefault();
        if (marked.IsFailure)
            return marked;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
