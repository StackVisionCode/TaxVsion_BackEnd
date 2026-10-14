namespace BuildingBlocks.Platform;

/// <summary>
/// Marca y dominio de la plataforma tal como los publica Tenant (agregado <c>PlatformSettings</c>).
/// Es lo que cualquier servicio lee para componer un link, un asunto o un PDF en vez de llevar
/// "TaxOffix" o "taxoffix.io" escritos a mano. <see cref="Version"/> 0 = valores locales de
/// arranque (todavía no llegó la versión de Tenant).
/// </summary>
public sealed record PlatformSettingsSnapshot(
    long Version,
    string BrandName,
    string LegalName,
    string BaseDomain,
    string ApiHost,
    string SupportEmail,
    string BillingEmail
)
{
    /// <summary>CRM del personal y login central (<c>app.{BaseDomain}</c>).</summary>
    public string AppHost => $"app.{BaseDomain}";

    /// <summary>Portal de clientes (<c>client.{BaseDomain}</c>).</summary>
    public string ClientHost => $"client.{BaseDomain}";

    /// <summary>Sitio público de la plataforma.</summary>
    public string Website => $"https://{BaseDomain}";

    /// <summary>Host de una oficina por su slug (<c>{slug}.{BaseDomain}</c>).</summary>
    public string TenantHost(string slug) => $"{slug}.{BaseDomain}";

    /// <summary>
    /// Subdominios de la plataforma que nunca son una oficina. Un slug así no se puede registrar, y
    /// estos hosts no se resuelven como tenant.
    /// </summary>
    public static IReadOnlySet<string> ReservedSubdomains { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "api", "app", "www", "admin", "client" };

    /// <summary>
    /// Si el host es el de una oficina (<c>{slug}.{BaseDomain}</c>, una sola etiqueta y no reservada),
    /// devuelve el slug. Es la única regla de "esto es un host de oficina": Auth y el Gateway la usan
    /// igual, así que no pueden discrepar sobre qué host es de quién.
    /// </summary>
    public bool TryGetTenantSlug(string? host, out string slug)
    {
        slug = string.Empty;
        var normalized = NormalizeHost(host);
        var suffix = "." + NormalizeHost(BaseDomain);
        if (normalized.Length <= suffix.Length || !normalized.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var label = normalized[..^suffix.Length];
        if (label.Contains('.') || ReservedSubdomains.Contains(label))
            return false;

        slug = label;
        return true;
    }

    /// <summary>
    /// Hosts propios de la plataforma que no son de ninguna oficina: el apex y los subdominios
    /// reservados (incluido el de la API).
    /// </summary>
    public bool IsSharedHost(string? host)
    {
        var normalized = NormalizeHost(host);
        if (normalized.Length == 0)
            return false;

        var baseDomain = NormalizeHost(BaseDomain);
        if (normalized == baseDomain || normalized == NormalizeHost(ApiHost))
            return true;

        var suffix = "." + baseDomain;
        return normalized.EndsWith(suffix, StringComparison.Ordinal)
            && ReservedSubdomains.Contains(normalized[..^suffix.Length]);
    }

    /// <summary>Orígenes del landing (apex y www): los que pueden usar la sesión del Account.</summary>
    public IReadOnlyList<string> LandingOrigins
    {
        get
        {
            var domain = NormalizeHost(BaseDomain);
            return [$"https://{domain}", $"https://www.{domain}"];
        }
    }

    /// <summary>Orígenes de todas las apps web de la plataforma: landing, CRM (app) y portal (client).</summary>
    public IReadOnlyList<string> WebOrigins
    {
        get
        {
            var domain = NormalizeHost(BaseDomain);
            return [.. LandingOrigins, $"https://app.{domain}", $"https://client.{domain}"];
        }
    }

    /// <summary>Si el origen (esquema + host, sin barra final) es uno de los dados. Sin distinguir mayúsculas.</summary>
    public static bool IsOriginIn(string? origin, IEnumerable<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;
        var candidate = origin.Trim().TrimEnd('/');
        return allowed.Any(o => string.Equals(o.Trim().TrimEnd('/'), candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Si el origen es HTTPS y el host es uno de la plataforma: apex, subdominio reservado (api, app,
    /// www, admin, client) o subdominio de oficina (<c>{slug}.{BaseDomain}</c>). Es la misma regla que
    /// usa <c>TenantHostGuardMiddleware</c>, aplicada al predicado de CORS para que el gateway acepte
    /// cualquier subdominio nuevo sin tocar <see cref="WebOrigins"/>.
    /// </summary>
    public bool IsPlatformHttpsOrigin(string? origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return TryGetTenantSlug(uri.Host, out _) || IsSharedHost(uri.Host);
    }

    /// <summary>Minúsculas, sin espacios, sin puerto y sin el punto final.</summary>
    public static string NormalizeHost(string? host)
    {
        var value = host?.Trim().ToLowerInvariant() ?? string.Empty;
        var colon = value.IndexOf(':');
        if (colon >= 0)
            value = value[..colon];
        return value.TrimEnd('.');
    }
}

/// <summary>La versión vigente de <see cref="PlatformSettingsSnapshot"/> en este proceso.</summary>
public interface IPlatformSettings
{
    PlatformSettingsSnapshot Current { get; }
}
