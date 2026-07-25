using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Wolverine;

namespace BuildingBlocks.Tenancy;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — setea el <see cref="TenantContext"/> scoped-request a
/// partir del claim <c>tenant_id</c> del JWT autenticado, para que el <c>HasQueryFilter</c> global
/// de cada <c>*DbContext</c> tenga el tenant listo antes de que la request llegue al controller.
/// Extraído de <c>TaxVision.Growth.Api.Common.JwtTenantContextMiddleware</c> (versión más completa
/// de las dos que ya existían — la otra, en Signature, no rechazaba un claim malformado ni
/// propagaba el tenant a Wolverine) a BuildingBlocks.Web para que los demás servicios no dupliquen
/// la misma lógica.
///
/// <para>
/// Registrar con <c>app.UseMiddleware&lt;JwtTenantContextMiddleware&gt;()</c> después de
/// <c>UseAuthentication</c>/<c>UseAuthorization</c>. Requests sin claim <c>tenant_id</c> (anónimas,
/// M2M sin tenant, JWKS) simplemente no llenan el tenant — el filtro fail-closed de cada DbContext
/// filtra por <see cref="System.Guid.Empty"/> en ese caso (0 filas para entidades tenant-owned);
/// los flujos anónimos legítimos que necesitan ver datos sin tenant usan
/// <c>IgnoreQueryFilters()</c> explícito en el repo, no dependen de este middleware.
/// </para>
///
/// <para>
/// <b>Necesario, pero no suficiente</b>: casi todos los controllers despachan sus comandos vía
/// <c>bus.InvokeAsync(command)</c>, y Wolverine ejecuta ese handler en un DI scope NUEVO,
/// desconectado del scope de la request donde este middleware corrió — el <see cref="TenantContext"/>
/// que ve el handler es una instancia distinta, vacía de nuevo. Por eso este middleware también
/// estampa <see cref="IMessageBus.TenantId"/>: Wolverine copia ese valor a
/// <see cref="Envelope.TenantId"/> del envelope resultante, y <see cref="LocalCommandTenantMiddleware"/>
/// (registrado globalmente en cada Program.cs) lo lee de vuelta dentro del scope del handler. Sin
/// ambas piezas, cualquier entidad <c>ITenantOwned</c> consultada dentro de un handler devolvería
/// 0 filas SIEMPRE bajo la política fail-closed — no solo en jobs de background, en cualquier
/// request autenticada normal.
/// </para>
/// </summary>
public sealed class JwtTenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TenantContext tenantContext, IMessageBus bus)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = ctx.User.FindFirst("tenant_id");
            if (tenantClaim is not null && !ctx.User.TryGetTenantId(out _))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (ctx.User.TryGetTenantId(out var tenantId))
            {
                tenantContext.SetTenant(tenantId);
                bus.TenantId = tenantId.ToString();
            }
        }

        await next(ctx);
    }
}
