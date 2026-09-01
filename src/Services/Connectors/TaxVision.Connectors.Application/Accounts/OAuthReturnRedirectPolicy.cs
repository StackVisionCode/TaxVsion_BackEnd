namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Decide a qué base redirige el callback de OAuth: el origen del frontend que inició el flujo
/// (subdominio del tenant, donde el usuario sigue logueado) si es válido y de un host permitido; si no,
/// el BaseUrl configurado. Es seguridad: un origen no validado sería un open redirect (el callback es
/// anónimo y el <c>state</c> lo controla el usuario). Se conserva SOLO scheme://host[:port] — nunca el
/// path/query recibido.
/// </summary>
public static class OAuthReturnRedirectPolicy
{
    public static string Resolve(string? returnOrigin, string baseUrl)
    {
        var fallback = (baseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(returnOrigin))
            return fallback;
        if (!Uri.TryCreate(returnOrigin, UriKind.Absolute, out var origin))
            return fallback;
        if (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps)
            return fallback;
        if (!IsAllowedHost(origin.Host, baseUrl))
            return fallback;
        return origin.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// Permite el mismo host del BaseUrl y cualquier subdominio de su dominio registrable (p. ej.
    /// *.taxproffice.com). localhost solo se acepta cuando el BaseUrl TAMBIÉN es local (dev) — nunca
    /// en prod, para no redirigir a la máquina del usuario desde un dominio de producción.
    /// </summary>
    public static bool IsAllowedHost(string host, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return false;
        if (IsLocal(baseUri.Host) && IsLocal(host))
            return true;
        if (host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
            return true;
        var registrable = RegistrableDomain(baseUri.Host);
        return registrable is not null
            && (
                host.Equals(registrable, StringComparison.OrdinalIgnoreCase)
                // El "." previo evita la confusión de sufijo: "eviltaxproffice.com" NO termina en ".taxproffice.com".
                || host.EndsWith("." + registrable, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static bool IsLocal(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1";

    /// <summary>Heurística de 2 labels (dominio.tld) — suficiente para los dominios propios de TaxVision.</summary>
    private static string? RegistrableDomain(string host)
    {
        var labels = host.Split('.');
        return labels.Length >= 2 ? $"{labels[^2]}.{labels[^1]}" : null;
    }
}
