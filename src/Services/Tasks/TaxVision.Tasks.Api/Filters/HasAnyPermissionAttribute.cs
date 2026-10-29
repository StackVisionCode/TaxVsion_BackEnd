using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaxVision.Tasks.Api.Filters;

// Basta con uno de los permisos. Apilar varios [HasPermission] exigiría todos a la vez.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HasAnyPermissionAttribute(params string[] permissions) : Attribute, IAsyncAuthorizationFilter
{
    public IReadOnlyList<string> Permissions { get; } = permissions;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var source = services.GetRequiredService<IUserPermissionsSource>();
        foreach (var permission in Permissions)
        {
            if (
                !await source.HasPermissionAsync(
                    context.HttpContext.User,
                    permission,
                    context.HttpContext.RequestAborted
                )
            )
                continue;
            services.GetRequiredService<AuthorizationMetrics>().RecordDecision(true, "1");
            return;
        }

        services.GetRequiredService<AuthorizationMetrics>().RecordDecision(false, "1");
        var denial = AuthorizationDenial.ForPermission(string.Join("|", Permissions));
        denial.Record(context.HttpContext);
        context.Result = new ObjectResult(denial.ToProblemDetails(context.HttpContext))
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
    }
}
