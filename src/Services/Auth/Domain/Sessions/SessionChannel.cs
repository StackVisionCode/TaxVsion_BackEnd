namespace TaxVision.Auth.Domain.Sessions;

/// <summary>
/// Por dónde entró la sesión. La sesión única se cuenta por canal: una web (CRM, portal o Account) y una
/// móvil pueden convivir; dos del mismo canal piden takeover.
/// </summary>
public enum SessionChannel
{
    Web = 0,
    Mobile = 1,
}
