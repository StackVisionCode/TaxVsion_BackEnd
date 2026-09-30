using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace BuildingBlocks.Web.Results;

/// <summary>Un <see cref="Error"/> con espera (throttle de dominio) sale con la cabecera
/// <c>Retry-After</c>, igual que los 429 de los limiters HTTP.</summary>
public sealed class RetryAfterHeaderFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: Error { RetryAfterSeconds: > 0 } error })
            context.HttpContext.Response.Headers[HeaderNames.RetryAfter] = error.RetryAfterSeconds.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
