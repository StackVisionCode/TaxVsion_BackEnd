namespace TaxVision.Auth.Domain.Users;

/// <summary>
/// Ciclo de vida del usuario. <see cref="IsActive"/> sigue siendo la compuerta de runtime (login/refresh);
/// este estado solo añade la distinción terminal. Invariante: <c>Offboarded ⇒ IsActive == false</c>.
/// </summary>
public enum UserStatus
{
    /// <summary>Activo.</summary>
    Active,

    /// <summary>Baja reversible (desactivado): puede reactivarse.</summary>
    Deactivated,

    /// <summary>Retirado del tenant: estado TERMINAL, no reversible.</summary>
    Offboarded,
}
