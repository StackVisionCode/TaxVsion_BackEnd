namespace TaxVision.Connectors.Application.OAuth;

/// <summary>Códigos de error de <see cref="IOAuthTokenManager"/> que los callers necesitan distinguir.</summary>
public static class OAuthTokenErrorCodes
{
    public const string NoToken = "OAuthTokenManager.NoToken";
    public const string LockTimeout = "OAuthTokenManager.LockTimeout";
    public const string RefreshFailed = "OAuthTokenManager.RefreshFailed";
    public const string CircuitOpen = "OAuthTokenManager.CircuitOpen";

    /// <summary>El proveedor no respondió bien (5xx, 429, red) o rechazó las credenciales de la app.</summary>
    public const string ProviderUnavailable = "OAuthTokenManager.ProviderUnavailable";

    /// <summary>
    /// El refresh token ya no sirve (o no hay): reintentar no lo arregla, solo reconectar la cuenta.
    /// El breaker abierto, el timeout del lock y las caídas del proveedor son transitorios y no entran acá.
    /// </summary>
    public static bool RequiresReconnect(string code) => code is RefreshFailed or NoToken;
}
