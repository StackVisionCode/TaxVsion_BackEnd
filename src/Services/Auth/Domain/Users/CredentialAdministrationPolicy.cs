using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Users;

/// <summary>
/// Quién de la oficina puede actuar sobre la credencial de quién (03 §6.4). Reglas en orden: la primera que falla
/// decide. Las de "no existe" devuelven <c>User.NotFound</c> para no enumerar. Sin dependencias de infraestructura.
/// </summary>
public static class CredentialAdministrationPolicy
{
    public static Result CanActOn(
        User actor,
        User target,
        CredentialAdministrationAction action,
        UserAccountKind expectedKind,
        bool ownerOnly
    )
    {
        if (actor.TenantId != target.TenantId)
            return Result.Failure(CredentialAdministrationErrors.NotFound);
        if (target.AccountKind != expectedKind)
            return Result.Failure(CredentialAdministrationErrors.NotFound);
        if (actor.ActorType != UserActorType.TenantAdmin || !actor.IsActive)
            return Result.Failure(CredentialAdministrationErrors.ActorNotAllowed);
        if (ownerOnly && !actor.IsOwner)
            return Result.Failure(CredentialAdministrationErrors.OwnerOnly);
        if (actor.Id == target.Id)
            return Result.Failure(CredentialAdministrationErrors.SelfNotAllowed);
        if (target.IsOwner)
            return Result.Failure(CredentialAdministrationErrors.TargetProtected);
        if (target.ActorType == UserActorType.PlatformAdmin)
            return Result.Failure(CredentialAdministrationErrors.NotFound);
        if (target.ActorType == UserActorType.TenantAdmin && !actor.IsOwner)
            return Result.Failure(CredentialAdministrationErrors.Hierarchy);
        if (target.Status != UserStatus.Active || !target.IsActive)
            return Result.Failure(CredentialAdministrationErrors.TargetInactive);
        // ResetMfa sin factores no es error: el handler responde Outcome=NotEnabled.
        if (action == CredentialAdministrationAction.SendResetLink && !target.EmailVerified)
            return Result.Failure(CredentialAdministrationErrors.EmailUnverified);
        return Result.Success();
    }
}
