using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Domain.Offboarding;

/// <summary>Quién se puede retirar, quién puede recibir su trabajo y qué queda en la ficha.</summary>
public static class OffboardingPolicy
{
    /// <summary>
    /// El retiro es para quien TRABAJA en la oficina: reasigna su trabajo a un sucesor, transfiere sus archivos
    /// compartidos, suelta sus conectores y libera su asiento. Un cliente de portal no tiene nada de eso, y su
    /// acceso se quita desde su propio perfil. Se corta acá y no solo en la pantalla.
    /// </summary>
    public static Result EnsureBelongsToTheOffice(User target) =>
        target.ActorType == UserActorType.CustomerPortal
            ? Result.Failure(
                new Error(
                    "User.PortalClient",
                    "Portal clients aren't removed from the office. Manage their access from the client's profile."
                )
            )
            : Result.Success();

    /// <summary>Retirar a un admin ya suspendido no cambia cuántos admins activos quedan.</summary>
    public static bool ReducesActiveAdmins(User target) =>
        target.ActorType == UserActorType.TenantAdmin && target.IsActive;

    public static Result EnsureAnotherAdminRemains(int activeAdmins) =>
        activeAdmins <= 1
            ? Result.Failure(new Error("User.LastAdmin", "You cannot remove the last active administrator."))
            : Result.Success();

    /// <summary>
    /// Valida al sucesor y arma la ficha antes de tocar nada: un motivo inválido no deja un retiro a medias.
    /// Si viene sucesor, debe ser staff activo del mismo tenant.
    /// </summary>
    public static Result<OffboardingDetails> PrepareDetails(
        User target,
        Guid requestedByUserId,
        Guid? successorUserId,
        User? successor,
        OffboardingReason? reason,
        string? reasonNote
    )
    {
        if (successorUserId is { } successorId && !IsValidSuccessor(target, successorId, successor))
            return Result.Failure<OffboardingDetails>(
                new Error("User.Successor", "The successor must be an active employee of this tenant.")
            );

        return OffboardingDetails.Create(requestedByUserId, successorUserId, reason, reasonNote);
    }

    private static bool IsValidSuccessor(User target, Guid successorId, User? successor) =>
        successorId != target.Id
        && successor is not null
        && successor.TenantId == target.TenantId
        && successor.IsActive
        && successor.ActorType is UserActorType.TenantEmployee or UserActorType.TenantAdmin;
}
