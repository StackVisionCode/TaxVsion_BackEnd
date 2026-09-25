namespace BuildingBlocks.Web.RateLimiting;

/// <summary>
/// Capa 2 del modelo de rate limiting: el gate pre-auth del Gateway. Vive en configuración
/// (<c>GatewayRateLimiting</c>) y no en C# — añadir un endpoint sensible no debería exigir
/// recompilar y redesplegar el Gateway. Ver GW-12 del plan de remediación.
///
/// <para>
/// Los valores por defecto de esta clase reproducen exactamente el comportamiento que estaba
/// hardcodeado, así que un despliegue sin la sección se comporta igual que antes. Es deliberado:
/// esto es un gate de seguridad, y el modo degradado tiene que ser el conocido, no "sin límite".
/// </para>
/// </summary>
public sealed class GatewayRateLimitOptions
{
    public const string SectionName = "GatewayRateLimiting";

    /// <summary>
    /// Endpoints alcanzables sin JWT (login, MFA, reset de password, aceptar invitación, registro
    /// de tenant). Se particionan por <c>IP + path</c> porque no hay identidad todavía, así que el
    /// cupo tiene que alcanzar para una oficina entera detrás de un NAT; el brute force de una cuenta
    /// lo frena Auth por email/cuenta. Fuera de aquí: <c>/auth/refresh</c> (un 429 deslogueaba al
    /// usuario; lo acota Auth con su propio limiter) y el <c>/auth/invitations</c> autenticado del
    /// admin (lista/creación, con su política tiered).
    /// </summary>
    public GatewayRateLimitGroup PreAuthByIp { get; set; } =
        new()
        {
            PermitLimit = 30,
            WindowSeconds = 60,
            Rules =
            [
                new GatewayRateLimitRule { Pattern = "/auth/login" },
                new GatewayRateLimitRule { Pattern = "/auth/mfa/verify" },
                new GatewayRateLimitRule { Pattern = "/auth/password/forgot" },
                new GatewayRateLimitRule { Pattern = "/auth/password/reset" },
                new GatewayRateLimitRule { Pattern = "/auth/me/email/confirm" },
                new GatewayRateLimitRule { Pattern = "/auth/invitations/accept" },
                new GatewayRateLimitRule { Pattern = "/tenants", Method = "POST" },
            ],
        };

    /// <summary>
    /// Cuota por <c>tenant_id</c> del JWT (IP como fallback) para rutas de CloudStorage. Sin reglas por
    /// defecto: la subida ya la acota <c>cloudstorage.i.upload</c> en el servicio (distribuido en Redis),
    /// y esta cuota era un segundo límite en memoria, por réplica, que duplicaba el castigo. Se deja el
    /// grupo para poder reactivarlo por configuración en un incidente.
    /// </summary>
    public GatewayRateLimitGroup StorageUploadByTenant { get; set; } =
        new()
        {
            PermitLimit = 30,
            WindowSeconds = 60,
            Rules = [],
        };
}

/// <summary>Un grupo de rutas que comparten cuota y forma de particionar.</summary>
public sealed class GatewayRateLimitGroup
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public IReadOnlyList<GatewayRateLimitRule> Rules { get; set; } = [];
}

/// <summary>
/// Una ruta del gate pre-auth. <see cref="Pattern"/> compara segmento a segmento y admite <c>*</c>
/// como comodín de un segmento completo (<c>/storage/files/*/complete</c>); <see cref="Method"/>
/// vacío significa "cualquier método".
/// </summary>
public sealed class GatewayRateLimitRule
{
    public string Pattern { get; set; } = string.Empty;

    public string? Method { get; set; }

    public bool Matches(string path, string method)
    {
        if (!string.IsNullOrEmpty(Method) && !Method.Equals(method, StringComparison.OrdinalIgnoreCase))
            return false;

        var patternSegments = Pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (patternSegments.Length != pathSegments.Length)
            return false;

        for (var i = 0; i < patternSegments.Length; i++)
        {
            if (patternSegments[i] == "*")
                continue;

            if (!patternSegments[i].Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
