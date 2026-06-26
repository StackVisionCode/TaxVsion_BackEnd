using Microsoft.AspNetCore.Http;
using Serilog.Context;
using BuildingBlocks.Common;

namespace BuildingBlocks.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{

    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext ctx, ICorrelationContext corr)
    {
        // Check if the incoming request has a CorrelationId header. If it does, use that value; otherwise, generate a new CorrelationId.
        var id = ctx.Request.Headers[Header].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString("N");
        }

        ctx.Request.Headers[Header] = id;
        corr.Set(id);

        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(ctx);
        }



    }
}