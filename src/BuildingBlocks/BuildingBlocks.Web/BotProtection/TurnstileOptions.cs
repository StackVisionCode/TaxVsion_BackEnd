namespace BuildingBlocks.Web.BotProtection;

/// <summary>
/// Configuración de Turnstile de un servicio. Sin <see cref="SiteKey"/>, <see cref="SecretKey"/> o
/// <see cref="ExpectedAction"/> la pieza no está lista y todo token se rechaza (fail-closed).
/// </summary>
public sealed class TurnstileOptions
{
    public const string DefaultSiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public string? SiteKey { get; set; }

    public string? SecretKey { get; set; }

    public string SiteVerifyUrl { get; set; } = DefaultSiteVerifyUrl;

    /// <summary>Acción que el widget declara; un token de otro formulario no sirve aquí.</summary>
    public string? ExpectedAction { get; set; }

    /// <summary>Si tiene valores, el token debe venir de uno de estos hosts.</summary>
    public List<string> ExpectedHostnames { get; set; } = [];

    /// <summary>Acepta las claves de prueba de Cloudflare (siempre pasan). Solo para desarrollo.</summary>
    public bool AllowTestingKeys { get; set; }

    public int TimeoutSeconds { get; set; } = 5;
}
