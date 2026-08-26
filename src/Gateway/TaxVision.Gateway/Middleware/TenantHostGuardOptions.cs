namespace TaxVision.Gateway.Middleware;

/// <summary>
/// Configuración de <see cref="TenantHostGuardMiddleware"/>. El senior pidió subir al Gateway la
/// validación Host↔tenant que hasta ahora vivía repartida (Auth 404ea hosts desconocidos; los
/// servicios confían en el tenant del JWT sin mirar el Host).
/// </summary>
public sealed class TenantHostGuardOptions
{
    public const string SectionName = "TenantHostGuard";

    /// <summary>Interruptor maestro. En <c>false</c> el middleware deja pasar todo sin validar.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Dominio base de las oficinas (ej. <c>taxproffice.com</c>). Un Host que no termine en
    /// <c>.{BaseDomain}</c> — el apex, <c>localhost</c>, un dominio ajeno — se trata como host de
    /// sistema y no se valida. Vacío ⇒ el middleware no valida ningún host (útil en dev).
    /// </summary>
    public string BaseDomain { get; init; } = "";

    /// <summary>Subdominios que NO son oficinas (host de sistema/branding): tomarlos como oficina
    /// mandaría a resolver un tenant inexistente.</summary>
    public string[] SystemSubdomains { get; init; } = ["api", "app", "www", "admin"];

    /// <summary>TTL de cache para resoluciones positivas (host → tenant). Los subdominios casi nunca cambian.</summary>
    public TimeSpan PositiveCacheTtl { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>TTL de cache para negativos (host no registrado) — corto, para que una oficina recién
    /// dada de alta aparezca pronto sin esperar el TTL largo.</summary>
    public TimeSpan NegativeCacheTtl { get; init; } = TimeSpan.FromMinutes(1);
}
