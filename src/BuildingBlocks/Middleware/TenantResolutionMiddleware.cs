using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Middleware;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TenantContext tenant)
    {
        var raw = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (Guid.TryParse(raw, out var tenantId))
            tenant.SetTenant(tenantId);
        await next(ctx);
    }

}
