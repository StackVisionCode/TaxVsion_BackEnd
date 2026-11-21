using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaxVision.CloudStorage.Api.Common;

/// <summary>
/// 403 para PlatformAdmin aunque <see cref="AllowActorTypesAttribute"/> y <c>[HasPermission]</c> lo dejen pasar
/// siempre. Para superficies que son solo de la oficina.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ExcludePlatformAdminAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.User.IsPlatformAdmin())
            return;
        context.Result = new ObjectResult(AuthorizationDenial.ActorTypeNotAllowed.ToProblemDetails(context.HttpContext))
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
    }
}
