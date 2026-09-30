namespace TaxVision.Auth.Domain.Users;

/// <summary>
/// Cuenta de login según su audiencia. Staff (empleado, admin, plataforma) y Portal (cliente) son cuentas
/// separadas aunque compartan email en la misma oficina: cada una tiene su contraseña, su MFA y su sesión.
/// Todo flujo que busca un usuario por email (login, reset, invitación, cambio de email) dice de qué cuenta
/// habla, y el email es único por oficina dentro de cada tipo.
/// </summary>
public enum UserAccountKind
{
    Staff,
    Portal,
}

public static class UserAccountKinds
{
    public static UserAccountKind Of(UserActorType actorType) =>
        actorType == UserActorType.CustomerPortal ? UserAccountKind.Portal : UserAccountKind.Staff;
}
