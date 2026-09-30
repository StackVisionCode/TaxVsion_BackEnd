using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// Permiso exigido solo a un actor type, en endpoints que comparten varios. Apilar dos
/// <see cref="HasPermissionAttribute"/> no sirve: ASP.NET exige TODAS las policies, así que el
/// staff quedaría fuera por no tener el permiso del cliente. Acá el chequeo solo corre cuando el
/// llamador es el actor declarado; para cualquier otro el atributo no existe.
/// </summary>
/// <remarks>
/// Requiere que el servicio registre <see cref="IUserPermissionsSource"/>
/// (<c>AddUserPermissionsSource</c>) y el filtro global (<c>AddActorTypeAuthorization</c>, que
/// aporta <see cref="AuthorizationMetrics"/>). Es un complemento de
/// <see cref="HasPermissionAttribute"/>, no un reemplazo: el permiso común del endpoint se sigue
/// declarando con el atributo de siempre.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionForActorAttribute(ActorType actorType, string permission)
    : Attribute,
        IAsyncAuthorizationFilter
{
    public ActorType ActorType { get; } = actorType;

    public string Permission { get; } = permission;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // PlatformAdmin nunca coincide con el actor declarado, así que pasa de largo — mismo
        // criterio que ActorTypeAuthorizationFilter.
        if (context.HttpContext.User.GetActorType() != ActorType)
            return;

        var source = context.HttpContext.RequestServices.GetRequiredService<IUserPermissionsSource>();
        var allowed = await source.HasPermissionAsync(
            context.HttpContext.User,
            Permission,
            context.HttpContext.RequestAborted
        );

        context.HttpContext.RequestServices.GetRequiredService<AuthorizationMetrics>().RecordDecision(allowed, "1");
        if (!allowed)
            context.Result = new ForbidResult();
    }
}
