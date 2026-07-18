using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Http;

namespace TaxVision.Signature.Api.Common;

/// <summary>
/// Setea el <see cref="TenantContext"/> a partir del claim <c>tenant_id</c> del JWT
/// autenticado. Se ejecuta después de <c>UseAuthentication</c> y antes de que llegue
/// al controller — así el <c>SignatureDbContext.HasQueryFilter</c> global tiene el
/// tenant listo para filtrar.
///
/// <para>
/// Endpoints anónimos (JWKS, /signature/public/*) no llenan el tenant — el filtro
/// global degrada a "no aplica" (<c>!HasTenant</c>) y el flujo público sigue funcionando
/// porque los handlers públicos resuelven el tenant desde el token firmado.
/// </para>
/// </summary>
public sealed class JwtTenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TenantContext tenantContext)
    {
        if (ctx.User.TryGetTenantId(out var tenantId))
            tenantContext.SetTenant(tenantId);
        await next(ctx);
    }
}
