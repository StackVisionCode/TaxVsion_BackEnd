using Microsoft.Extensions.Options;
using TaxVision.Gateway.Observability;

namespace TaxVision.Gateway.Middleware;

/// <summary>
/// Valida en el Gateway la relación Host↔tenant (pedido del senior):
/// <list type="number">
///   <item>Subdominio de oficina <b>no registrado</b> → 404 con mensaje plano (sin filtrar detalle de API).</item>
///   <item><c>tenant_id</c> del JWT <b>distinto</b> al tenant del Host → 403 (acceso cruzado entre oficinas).</item>
/// </list>
/// Los hosts de sistema (<c>api.*</c>, apex, subdominios reservados, <c>localhost</c>) pasan sin validar.
/// Fail-open: si Auth no responde, la request pasa — un hipo de Auth no debe tumbar el tráfico de todas
/// las oficinas; solo un 404 definitivo de "no registrado" bloquea. Corre después de
/// <c>UseAuthentication</c> (necesita el JWT ya parseado) y antes del <c>MapReverseProxy</c>.
/// </summary>
public sealed class TenantHostGuardMiddleware(
    RequestDelegate next,
    IOptions<TenantHostGuardOptions> options,
    ILogger<TenantHostGuardMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext context, IHostTenantResolver resolver)
    {
        var opts = options.Value;
        var host = context.Request.Host.Host;

        // CORS preflight no lleva credenciales ni debe bloquearse acá; el host de sistema tampoco se valida.
        if (!opts.Enabled || HttpMethods.IsOptions(context.Request.Method) || !IsTenantSubdomain(host, opts))
        {
            await next(context);
            return;
        }

        var result = await resolver.ResolveAsync(host, context.RequestAborted);

        switch (result.Outcome)
        {
            case HostTenantOutcome.NotRegistered:
                logger.LogWarning("Host de oficina no registrado bloqueado: {Host}", host);
                GatewayMetrics.TenantHostRejected.Add(1);
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "office_not_found",
                    "This office is not available."
                );
                return;

            case HostTenantOutcome.Resolved:
                var jwtTenant = context.User.FindFirst("tenant_id")?.Value;
                if (
                    !string.IsNullOrEmpty(jwtTenant)
                    && Guid.TryParse(jwtTenant, out var jwtTenantId)
                    && jwtTenantId != result.TenantId
                )
                {
                    logger.LogWarning(
                        "Acceso cruzado bloqueado: tenant del JWT {JwtTenant} != tenant del Host {HostTenant} ({Host})",
                        jwtTenantId,
                        result.TenantId,
                        host
                    );
                    GatewayMetrics.TenantHostCrossTenantBlocked.Add(1);
                    await WriteProblemAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        "tenant_mismatch",
                        "You don't have access to this office."
                    );
                    return;
                }
                break;

            case HostTenantOutcome.Unavailable:
                // Fail-open (decisión de operación confirmada): un fallo de Auth no bloquea el tráfico.
                break;
        }

        await next(context);
    }

    /// <summary>
    /// <c>true</c> si el Host es un subdominio de oficina (no <c>api/app/www/admin</c>, ni el apex,
    /// ni <c>localhost</c> ni un dominio ajeno). Sin <c>BaseDomain</c> configurado, nada se valida.
    /// </summary>
    private static bool IsTenantSubdomain(string host, TenantHostGuardOptions opts)
    {
        if (string.IsNullOrEmpty(opts.BaseDomain) || string.IsNullOrEmpty(host))
            return false;

        host = host.ToLowerInvariant();
        var suffix = "." + opts.BaseDomain.ToLowerInvariant();
        if (!host.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var slug = host[..^suffix.Length];
        if (slug.Length == 0 || slug.Contains('.'))
            return false;

        return !opts.SystemSubdomains.Contains(slug, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cuerpo mínimo y genérico — sin stack, sin status del upstream, sin nombres internos. El
    /// frontend muestra un mensaje plano; no debe poder inferir la topología de la API del error.
    /// </summary>
    private static async Task WriteProblemAsync(HttpContext context, int status, string error, string message)
    {
        // Nada se escribió aún (esto corre antes del proxy), así que no se limpia la respuesta:
        // se conservan los headers de seguridad/correlación ya puestos. WriteAsJsonAsync fija el
        // Content-Type. El cuerpo es genérico — sin status del upstream ni nombres internos.
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error, message });
    }
}
