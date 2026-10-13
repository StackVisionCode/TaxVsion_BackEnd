namespace TaxVision.Gateway.Middleware;

/// <summary>
/// Configuración de <see cref="TenantHostGuardMiddleware"/>. El senior pidió subir al Gateway la
/// validación Host↔tenant que hasta ahora vivía repartida (Auth 404ea hosts desconocidos; los
/// servicios confían en el tenant del JWT sin mirar el Host).
/// </summary>
/// <remarks>El dominio de las oficinas no vive acá: sale de <c>IPlatformSettings</c> (Tenant).</remarks>
public sealed class TenantHostGuardOptions
{
    public const string SectionName = "TenantHostGuard";

    /// <summary>Interruptor maestro. En <c>false</c> el middleware deja pasar todo sin validar.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Tope de vida de una resolución positiva (host → tenant): hasta cuánto se sigue sirviendo si Auth no
    /// contesta al revalidar.
    /// </summary>
    public TimeSpan PositiveCacheTtl { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Edad a partir de la cual una resolución positiva se vuelve a pedir a Auth en segundo plano (se sigue
    /// sirviendo la cacheada mientras tanto). Es lo que tarda en notarse un renombre o un dominio deshabilitado.
    /// </summary>
    public TimeSpan RevalidateAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>TTL de cache para negativos (host no registrado) — corto, para que una oficina recién
    /// dada de alta aparezca pronto sin esperar el TTL largo.</summary>
    public TimeSpan NegativeCacheTtl { get; init; } = TimeSpan.FromMinutes(1);
}
