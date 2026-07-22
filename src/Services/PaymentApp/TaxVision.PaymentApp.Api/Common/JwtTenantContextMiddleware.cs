using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Tenancy;

namespace TaxVision.PaymentApp.Api.Common;

/// <summary>
/// Setea el <see cref="TenantContext"/> a partir del claim <c>tenant_id</c> del JWT
/// autenticado. El header <c>X-Tenant-Id</c> se ignora por completo — un servicio de pagos
/// no confía en un header spoofeable (§42.3 del diseño). Se ejecuta después de
/// <c>UseAuthentication</c> y antes de <see cref="TenantStatusGateMiddleware"/>.
/// </summary>
public sealed class JwtTenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TenantContext tenantContext)
    {
        if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.TryGetTenantId(out var tenantId))
            tenantContext.SetTenant(tenantId);

        await next(ctx);
    }
}
