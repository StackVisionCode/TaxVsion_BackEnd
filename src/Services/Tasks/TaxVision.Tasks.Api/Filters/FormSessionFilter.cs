using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests;
using Wolverine;

namespace TaxVision.Tasks.Api.Filters;

public sealed class PublicFormSessionContext
{
    public ParticipantContext? Participant { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public string RequestHost { get; set; } = string.Empty;
}

public sealed class FormSessionFilter(IMessageBus bus, PublicFormSessionContext sessionContext) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var token = context.HttpContext.Request.Headers["X-Form-Session"].ToString();
        var host = PublicFormRequestHost.Resolve(context.HttpContext.Request);
        if (string.IsNullOrWhiteSpace(token) || host is null)
        {
            context.Result = Unauthorized();
            return;
        }
        var result = await bus.InvokeAsync<Result<ParticipantContext>>(
            new ResolvePublicFormSessionCommand(token, host, DateTime.UtcNow),
            context.HttpContext.RequestAborted
        );
        if (result.IsFailure)
        {
            context.Result = Unauthorized();
            return;
        }

        sessionContext.Participant = result.Value;
        sessionContext.SessionToken = token;
        sessionContext.RequestHost = host;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        await next();
    }

    private static ObjectResult Unauthorized() =>
        new(new Error("PublicForm.SessionInvalid", "The form session is invalid or expired."))
        {
            StatusCode = StatusCodes.Status401Unauthorized,
        };
}

internal static class PublicFormRequestHost
{
    internal static string? Resolve(HttpRequest request)
    {
        if (TryHost(request.Headers.Origin, out var originHost))
            return originHost;
        if (TryHost(request.Headers.Referer, out var refererHost))
            return refererHost;
        return request.Host.HasValue ? request.Host.Host.ToLowerInvariant() : null;
    }

    private static bool TryHost(string? value, out string host)
    {
        host = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        host = uri.Host.ToLowerInvariant();
        return host.Length > 0;
    }
}
