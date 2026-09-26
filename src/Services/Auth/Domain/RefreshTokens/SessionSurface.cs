namespace TaxVision.Auth.Domain.RefreshTokens;

/// <summary>
/// Superficie dueña de una cadena de refresh dentro de una sesión. El CRM (o el portal) y el Account del
/// Landing comparten el <c>sid</c> pero rotan cadenas separadas: revocar la sesión corta ambas, y el
/// logout del Account solo la suya.
/// </summary>
public enum SessionSurface
{
    /// <summary>CRM del staff o portal del cliente: la sesión de siempre.</summary>
    Workspace = 0,

    /// <summary>Account del Landing (gestión de la suscripción del TenantAdmin).</summary>
    Account = 1,
}
