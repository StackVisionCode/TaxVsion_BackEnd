using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.Api.Filters;

public sealed class ClientFormsAccessFilter(IClientFormsAvailabilityEvaluator availability) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!RequiresClientFormsAccess(context.ActionDescriptor))
        {
            await next();
            return;
        }

        context.HttpContext.User.TryGetTenantId(out var tenantId);
        var result = await availability.EvaluateAsync(tenantId, context.HttpContext.RequestAborted);
        if (result.Enabled)
        {
            await next();
            return;
        }

        context.Result = Denied(context.HttpContext, result.Reason!);
    }

    public static bool RequiresClientFormsAccess(Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor action)
    {
        if (action is not ControllerActionDescriptor descriptor)
            return false;
        if (descriptor.ControllerTypeInfo.Namespace?.Contains(".ClientForms", StringComparison.Ordinal) != true)
            return false;
        return !descriptor.MethodInfo.IsDefined(typeof(SkipClientFormsAccessAttribute), true)
            && !descriptor.ControllerTypeInfo.IsDefined(typeof(SkipClientFormsAccessAttribute), true);
    }

    private static ObjectResult Denied(HttpContext context, string code)
    {
        var problem = new ProblemDetails
        {
            Type = "https://taxvision.dev/problems/client-forms-availability",
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = "Client Forms is not available for this tenant.",
            Extensions =
            {
                ["code"] = code,
                ["reason"] = code,
                ["message"] = "Client Forms is not available for this tenant.",
                ["correlationId"] = context.Response.Headers[CorrelationIdMiddleware.Header].FirstOrDefault()
                    ?? context.Request.Headers[CorrelationIdMiddleware.Header].FirstOrDefault(),
            },
        };
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
    }
}
