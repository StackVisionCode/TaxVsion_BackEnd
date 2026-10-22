namespace BuildingBlocks.Platform;

/// <summary>
/// Decide a qué base redirige el callback de OAuth: el origen del frontend que inició el flujo
/// (subdominio del tenant, donde el usuario sigue logueado) si es válido y de un host permitido; si no,
/// el BaseUrl configurado. Es seguridad: un origen no validado sería un open redirect (el callback es
/// anónimo y el <c>state</c> lo controla el usuario). Se conserva SOLO scheme://host[:port] — nunca el
/// path/query recibido.
/// </summary>
public static class OAuthReturnRedirectPolicy
{
    public static string Resolve(string? returnOrigin, string baseUrl, string platformDomain)
    {
        var fallback = (baseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(returnOrigin))
            return fallback;
        if (!Uri.TryCreate(returnOrigin, UriKind.Absolute, out var origin))
            return fallback;
        if (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps)
            return fallback;
        if (!IsAllowedHost(origin.Host, baseUrl, platformDomain))
            return fallback;
        return origin.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// Igual que <see cref="Resolve"/>, pero devuelve una URL completa: el host del tenant (si es válido)
    /// con la ruta del <paramref name="fallbackUrl"/> configurado (p. ej. <c>/storage</c>).
    /// </summary>
    public static string ResolveUrl(string? returnOrigin, string fallbackUrl, string platformDomain)
    {
        var origin = Resolve(returnOrigin, fallbackUrl, platformDomain);
        if (
            origin == (fallbackUrl ?? string.Empty).TrimEnd('/')
            || !Uri.TryCreate(fallbackUrl, UriKind.Absolute, out var fallback)
        )
            return fallbackUrl ?? string.Empty;
        return origin + fallback.PathAndQuery;
    }

    /// <summary>
    /// Permite el mismo host del BaseUrl y el dominio de la plataforma con cualquier subdominio (p. ej.
    /// *.taxoffix.io; el dominio sale de PlatformSettings). localhost solo se acepta cuando el BaseUrl
    /// TAMBIÉN es local (dev) — nunca en prod, para no redirigir a la máquina del usuario desde un dominio
    /// de producción.
    /// </summary>
    public static bool IsAllowedHost(string host, string? baseUrl, string platformDomain)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return false;
        if (IsLocal(baseUri.Host) && IsLocal(host))
            return true;
        if (host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
            return true;
        var domain = platformDomain?.Trim().TrimEnd('.') ?? string.Empty;
        return domain.Length > 0
            && (
                host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                // El "." previo evita la confusión de sufijo: "eviltaxoffix.io" NO termina en ".taxoffix.io".
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static bool IsLocal(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1";
}
