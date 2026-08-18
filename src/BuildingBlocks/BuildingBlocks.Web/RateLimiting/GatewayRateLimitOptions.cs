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
    /// Endpoints alcanzables sin JWT (login, refresh, reset de password, aceptar invitación,
    /// registro de tenant). Se particionan por <c>IP + path</c> porque no hay identidad todavía.
    /// </summary>
    public GatewayRateLimitGroup PreAuthByIp { get; set; } =
        new()
        {
            PermitLimit = 10,
            WindowSeconds = 60,
            Rules =
            [
                new GatewayRateLimitRule { Pattern = "/auth/login" },
                new GatewayRateLimitRule { Pattern = "/auth/refresh" },
                new GatewayRateLimitRule { Pattern = "/auth/mfa/verify" },
                new GatewayRateLimitRule { Pattern = "/auth/password/forgot" },
                new GatewayRateLimitRule { Pattern = "/auth/password/reset" },
                new GatewayRateLimitRule { Pattern = "/auth/me/email/confirm" },
                new GatewayRateLimitRule { Pattern = "/auth/invitations/accept" },
                new GatewayRateLimitRule { Pattern = "/auth/invitations" },
                new GatewayRateLimitRule { Pattern = "/tenants", Method = "POST" },
            ],
        };

    /// <summary>
    /// Inicio y cierre de subida a CloudStorage. Se particiona por <c>tenant_id</c> del JWT (con la
    /// IP como fallback si aún no hay token): una cuota por IP castigaría a toda una oficina detrás
    /// de un NAT.
    /// </summary>
    public GatewayRateLimitGroup StorageUploadByTenant { get; set; } =
        new()
        {
            PermitLimit = 30,
            WindowSeconds = 60,
            Rules =
            [
                new GatewayRateLimitRule { Pattern = "/storage/files/uploads", Method = "POST" },
                new GatewayRateLimitRule { Pattern = "/storage/files/*/complete", Method = "POST" },
            ],
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
