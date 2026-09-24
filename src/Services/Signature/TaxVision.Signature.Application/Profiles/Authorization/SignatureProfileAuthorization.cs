using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Profiles.Authorization;

/// <summary>
/// Regla común de gestión de firmas: una firma personal solo la gestiona su dueño; una firma de
/// oficina (sin dueño) solo un admin. Un admin puede además gestionar cualquier firma personal.
/// </summary>
public static class SignatureProfileAuthorization
{
    private static readonly Error Forbidden = new("Signature.Profile.Forbidden", "You cannot manage this signature.");

    /// <summary>Valida por ámbito (al crear): oficina (null) exige admin; personal exige ser uno mismo.</summary>
    public static Result EnsureCanManageScope(Guid? ownerUserId, Guid actorUserId, bool actorIsAdmin)
    {
        if (ownerUserId is null)
            return actorIsAdmin ? Result.Success() : Result.Failure(Forbidden);
        if (ownerUserId == actorUserId || actorIsAdmin)
            return Result.Success();
        return Result.Failure(Forbidden);
    }

    /// <summary>Valida sobre una firma existente (al renombrar/archivar/borrar/marcar default).</summary>
    public static Result EnsureCanManage(SignatureProfile profile, Guid actorUserId, bool actorIsAdmin) =>
        EnsureCanManageScope(profile.OwnerUserId, actorUserId, actorIsAdmin);
}
