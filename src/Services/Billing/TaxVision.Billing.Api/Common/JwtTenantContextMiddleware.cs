using BuildingBlocks.Tenancy;
using Wolverine;

namespace TaxVision.Billing.Api.Common;

/// <summary>
/// Establece la identidad del tenant solo desde el JWT validado. Nunca se acepta X-Tenant-Id ni
/// valores del payload como autoridad de tenant (corrige el gap del CRM legado, que tomaba el
/// companyId de un query param).
/// </summary>
public sealed class JwtTenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext, IMessageBus bus)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = context.User.FindFirst("tenant_id");
            if (tenantClaim is not null && !context.User.TryGetTenantId(out _))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (context.User.TryGetTenantId(out var tenantId))
            {
                tenantContext.SetTenant(tenantId);
                bus.TenantId = tenantId.ToString();
            }
        }

        await next(context);
    }
}
