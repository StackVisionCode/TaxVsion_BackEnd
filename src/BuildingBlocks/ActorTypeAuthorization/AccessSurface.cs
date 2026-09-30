namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Superficie para la que Auth emitió el access token (claim <see cref="ClaimNames.Surface"/>). Un token
/// sin claim es el de siempre (CRM, portal). Uno con superficie solo entra a los endpoints que la
/// declaran con <c>[AllowSurface]</c>: un token que vive en el Landing no sirve para el resto del sistema.
/// </summary>
public static class AccessSurface
{
    /// <summary>Account del Landing (gestión de la suscripción del TenantAdmin).</summary>
    public const string Account = "account";
}
