using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Users;

/// <summary>Códigos de la administración de credenciales por la oficina (03 §15); su HTTP vive en ErrorHttpMapping.</summary>
public static class CredentialAdministrationErrors
{
    public static readonly Error NotFound = new("User.NotFound", "User does not exist in this tenant.");

    public static readonly Error ActorNotAllowed = new(
        "Auth.CredentialAdmin.ActorNotAllowed",
        "Only an active office administrator can manage credentials."
    );

    public static readonly Error OwnerOnly = new(
        "Auth.CredentialAdmin.OwnerOnly",
        "Only the account owner can manage credentials."
    );

    public static readonly Error SelfNotAllowed = new(
        "Auth.CredentialAdmin.SelfNotAllowed",
        "Use change or forgot password for your own account."
    );

    public static readonly Error TargetProtected = new(
        "Auth.CredentialAdmin.TargetProtected",
        "The account owner cannot be managed this way."
    );

    public static readonly Error Hierarchy = new(
        "Auth.CredentialAdmin.Hierarchy",
        "Only the account owner can manage another administrator."
    );

    public static readonly Error TargetInactive = new(
        "Auth.CredentialAdmin.TargetInactive",
        "Reactivate the user first."
    );

    public static readonly Error EmailUnverified = new(
        "Auth.CredentialAdmin.EmailUnverified",
        "The user's email address is not verified."
    );

    public static readonly Error InvalidTransition = new(
        "Auth.CredentialAdmin.InvalidTransition",
        "This action is not allowed in the account's current state."
    );

    public static readonly Error ReasonRequired = new("Auth.CredentialAdmin.ReasonRequired", "A reason is required.");

    public static readonly Error InvalidReason = new("Auth.CredentialAdmin.InvalidReason", "The reason is not valid.");

    public static readonly Error NoPendingLink = new(
        "Auth.CredentialAdmin.NoPendingLink",
        "There is no pending reset link."
    );

    public static readonly Error LinkAlreadyUsed = new(
        "Auth.CredentialAdmin.LinkAlreadyUsed",
        "The reset link was already used."
    );

    public static readonly Error TargetRateLimited = new(
        "Auth.CredentialAdmin.TargetRateLimited",
        "Too many credential actions for this user. Try again later."
    );

    public static readonly Error DeliveryUnavailable = new(
        "Auth.CredentialAdmin.DeliveryUnavailable",
        "The reset link cannot be delivered right now."
    );

    public static readonly Error StaleReauthentication = new(
        "Auth.CredentialAdmin.StaleReauthentication",
        "Confirm your identity again."
    );

    /// <summary>SR-03: falta el token del actor reenviado en el M2M, o no es válido o no cuadra con el pedido.</summary>
    public static readonly Error ActorTokenInvalid = new(
        "Auth.CredentialAdmin.ActorTokenInvalid",
        "We couldn't verify who made this request. Sign in again."
    );

    public static readonly Error EmailChangeCooldown = new(
        "Auth.CredentialAdmin.EmailChangeCooldown",
        "The email address changed recently. Try again later."
    );

    public static readonly Error PasswordChangeRequired = new(
        "Auth.PasswordChangeRequired",
        "You must update your password first."
    );

    public static readonly Error CredentialResetRequired = new(
        "User.CredentialResetRequired",
        "This account must reset its password with a link."
    );
}
