namespace TaxVision.Gateway.Middleware;

public sealed class TenantPropagationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        ctx.Request.Headers.Remove("X-Tenant-Id");

        var tenantId = ctx.User.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            ctx.Request.Headers["X-Tenant-Id"] = tenantId;
        }

        await next(ctx);
    }
}
