using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Api.Common;

/// <summary>
/// Rechaza cualquier request de un tenant que no esté activo (Suspended/Closed) o que sea el
/// Platform tenant (nunca sujeto de cobro). Corre después de
/// <see cref="JwtTenantContextMiddleware"/> y antes de los controllers — cierra la ventana en
/// la que un tenant recién suspendido podría seguir cobrando con un JWT todavía vigente.
/// </summary>
public sealed class TenantStatusGateMiddleware(RequestDelegate next)
{
    /// <summary><c>/admin/</c> exento a propósito: un PlatformAdmin opera desde el tenant
    /// Platform (que nunca pasa <c>CanOperate()</c>) y §42.6 del diseño pide que estas rutas
    /// sigan accesibles aunque el TENANT INVESTIGADO esté suspendido — el permiso
    /// <c>payment_client.admin.cross_tenant</c> ya es el gate de seguridad acá.</summary>
    // Sin barra final: PathString.StartsWithSegments espera el prefijo SIN ella para calzar
    // el límite de segmento correctamente — con barra nunca matchea el path real (bug real
    // encontrado probando el equivalente en PaymentApp con un PlatformAdmin).
    private static readonly string[] ExemptPathPrefixes =
        ["/health/live", "/health/ready", "/payments-client/webhooks", "/payments-client/checkout", "/payments-client/admin"];

    public async Task InvokeAsync(
        HttpContext ctx, ITenantContext tenantContext, ITenantRegistry tenants, ILogger<TenantStatusGateMiddleware> logger)
    {
        if (IsExempt(ctx.Request.Path))
        {
            await next(ctx);
            return;
        }

        if (!tenantContext.HasTenant)
        {
            await next(ctx);
            return;
        }

        var tenant = await tenants.GetByIdAsync(tenantContext.TenantId, ctx.RequestAborted);
        if (tenant is null)
        {
            logger.LogWarning("Request with unknown tenant {TenantId} rejected.", tenantContext.TenantId);
            await WriteForbiddenAsync(ctx, "Tenant.Unknown", "Tenant does not exist in the local registry.");
            return;
        }

        if (!tenant.CanOperate())
        {
            logger.LogInformation("Request for tenant {TenantId} rejected because status is {Status}.", tenant.Id, tenant.Status);
            await WriteForbiddenAsync(ctx, "Tenant.Inactive", "Tenant is not in a state that permits operations.");
            return;
        }

        await next(ctx);
    }

    private static bool IsExempt(PathString path)
    {
        foreach (var prefix in ExemptPathPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Task WriteForbiddenAsync(HttpContext ctx, string code, string message)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return ctx.Response.WriteAsJsonAsync(new { code, message, correlationId = ctx.TraceIdentifier });
    }
}
