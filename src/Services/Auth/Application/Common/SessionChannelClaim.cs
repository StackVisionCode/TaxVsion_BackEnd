namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Claim con el canal de la sesión. Solo lo llevan los tokens de las apps (<c>mobile</c>); ausente en la web.
/// No se usa <c>surface</c>: ese claim cierra el paso a todo endpoint que no lo declare con <c>[AllowSurface]</c>.
/// </summary>
public static class SessionChannelClaim
{
    public const string Type = "channel";
    public const string Mobile = "mobile";
}
