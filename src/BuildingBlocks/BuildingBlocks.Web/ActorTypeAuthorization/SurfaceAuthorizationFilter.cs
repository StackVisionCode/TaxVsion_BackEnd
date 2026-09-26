using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// Fail-closed por superficie: un token con claim <c>surface</c> solo entra a las acciones que la declaran
/// con <see cref="AllowSurfaceAttribute"/> (método o controller). Los tokens sin superficie (CRM, portal)
/// no se ven afectados. Se saltean las acciones anónimas y las de capability token, igual que
/// <see cref="ActorTypeAuthorizationFilter"/>.
/// </summary>
public sealed class SurfaceAuthorizationFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
            return;

        var surface = context.HttpContext.User.GetSurface();
        if (surface is null)
            return;

        if (
            HasAttribute<AllowAnonymousAttribute>(descriptor)
            || HasAttribute<AuthorizedByCapabilityTokenAttribute>(descriptor)
        )
            return;

        var declared =
            Find<AllowSurfaceAttribute>(descriptor.MethodInfo.GetCustomAttributes(inherit: true))
            ?? Find<AllowSurfaceAttribute>(descriptor.ControllerTypeInfo.GetCustomAttributes(inherit: true));
        if (declared is not null && declared.Surfaces.Contains(surface, StringComparer.Ordinal))
            return;

        context.Result = new ObjectResult(
            new Error("Auth.SurfaceNotAllowed", "This session can't be used for this action.")
        )
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }

    private static bool HasAttribute<T>(ControllerActionDescriptor descriptor)
        where T : Attribute =>
        descriptor.MethodInfo.GetCustomAttributes(typeof(T), inherit: true).Length > 0
        || descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(T), inherit: true).Length > 0;

    private static T? Find<T>(object[] attributes)
        where T : Attribute => attributes.OfType<T>().FirstOrDefault();
}
