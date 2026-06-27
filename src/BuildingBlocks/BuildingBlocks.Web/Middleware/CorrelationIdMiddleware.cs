using Microsoft.AspNetCore.Http;
using BuildingBlocks.Common;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace BuildingBlocks.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";
    private static readonly Regex ValidCorrelationId =
        new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task InvokeAsync(HttpContext ctx, ICorrelationContext corr)
    {
        // Check if the incoming request has a CorrelationId header. If it does, use that value; otherwise, generate a new CorrelationId.
        var id = ctx.Request.Headers[Header].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(id) || !ValidCorrelationId.IsMatch(id))
        {
            id = Guid.NewGuid().ToString("N");
        }

        ctx.Request.Headers[Header] = id;
        Activity.Current?.SetTag("taxvision.correlation_id", id);
        Activity.Current?.AddBaggage("taxvision.correlation_id", id);

        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (corr.Push(id))
        {
            await next(ctx);
        }
    }
}
