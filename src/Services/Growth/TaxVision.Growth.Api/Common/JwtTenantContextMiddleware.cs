using BuildingBlocks.Tenancy;
using Wolverine;

namespace TaxVision.Growth.Api.Common;

/// <summary>
/// Establishes tenant identity only from the validated JWT. X-Tenant-Id and request
/// payload values are never accepted as tenant authority.
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
                // Wolverine gives bus.InvokeAsync a fresh DI scope for the handler, so
                // TenantContext above doesn't reach it. Stamping IMessageBus.TenantId here
                // propagates onto Envelope.TenantId, which GrowthLocalCommandTenantMiddleware
                // reads back into the handler's own TenantContext.
                bus.TenantId = tenantId.ToString();
            }
        }

        await next(context);
    }
}
