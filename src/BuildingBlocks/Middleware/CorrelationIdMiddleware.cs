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
        var id = ctx.Request.Headers[Header].FirstOrDefault() ?? Guid.NewGuid().ToString();
        ((CorrelationContext)corr).Set(id);
        ctx.Response.Headers[Header] = id;

        // Push the CorrelationId into the Serilog LogContext so that it is included in all log entries for the duration of the request

        using (LogContext.PushProperty("CorrelationId", id))
            await next(ctx);



    }
}