using System.Text.Json;
using BuildingBlocks.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// Por qué se denegó, en un vocabulario cerrado. Es el campo <c>reason</c> del cuerpo del 403: el
/// frontend necesita distinguir "no tenés el permiso" de "tu plan no incluye el módulo" para mostrar
/// una pantalla comercial en vez de una de acceso restringido — hoy los dos llegan como un 403 vacío
/// e indistinguible.
/// </summary>
public static class AuthorizationDenialReasons
{
    /// <summary>Capa 3: el usuario no tiene el permiso que el endpoint exige.</summary>
    public const string Permission = "permission";

    /// <summary>Capa 2: el actor type del token no está entre los que el endpoint declara.</summary>
    public const string ActorType = "actor_type";

    /// <summary>Capa 2, fail-closed: el endpoint no declara ningún actor type. Es un bug del backend,
    /// no del caller — se distingue para poder alertarlo.</summary>
    public const string NotDeclared = "not_declared";

    /// <summary>Capa 1: el token es de otra superficie (Account del Landing).</summary>
    public const string Surface = "surface";

    /// <summary>El permiso está concedido pero su módulo no está en el plan del tenant.</summary>
    public const string Module = "module";
}

/// <summary>
/// Una denegación de autorización lista para serializar como RFC 9457. Las capas 1 y 2 (filtros de MVC)
/// la escriben directo; la capa 3 la deja en <see cref="HttpContext.Items"/> para que
/// <see cref="ProblemDetailsAuthorizationResultHandler"/> la encuentre cuando el middleware de
/// autorización convierta la policy fallida en un 403.
/// </summary>
/// <param name="Code">Código estable que el frontend switchea (<c>Authz.PermissionDenied</c>…).</param>
/// <param name="Reason">Uno de <see cref="AuthorizationDenialReasons"/>.</param>
/// <param name="Detail">Texto legible. Nunca nombra el permiso que falta si eso revelaría estructura
/// interna: el permiso va aparte, en <c>permission</c>, y solo cuando ya lo sabe el caller.</param>
/// <param name="Module">Módulo que falta, solo para <see cref="AuthorizationDenialReasons.Module"/>.</param>
/// <param name="Permission">Permiso exigido, cuando el endpoint lo declara explícitamente.</param>
public sealed record AuthorizationDenial(
    string Code,
    string Reason,
    string Detail,
    string? Module = null,
    string? Permission = null
)
{
    /// <summary>Clave en <c>HttpContext.Items</c>. Prefijada para no colisionar con claves de la app.</summary>
    public const string HttpContextItemKey = "buildingblocks.authz.denial";

    public static readonly AuthorizationDenial PermissionDenied = new(
        "Authz.PermissionDenied",
        AuthorizationDenialReasons.Permission,
        "You don't have permission to perform this action."
    );

    public static AuthorizationDenial ForPermission(string permission) =>
        PermissionDenied with
        {
            Permission = permission,
        };

    public static AuthorizationDenial ForModule(string module) =>
        new(
            "Authz.ModuleUnavailable",
            AuthorizationDenialReasons.Module,
            $"Your plan does not include the '{module}' module required for this action.",
            Module: module
        );

    public static readonly AuthorizationDenial ActorTypeNotAllowed = new(
        "Authz.ActorTypeNotAllowed",
        AuthorizationDenialReasons.ActorType,
        "This kind of account can't perform this action."
    );

    /// <summary>Endpoint sin <c>[AllowActorTypes]</c>: el filtro es fail-closed a propósito, pero el
    /// código y la razón lo separan de una denegación legítima para que se pueda alertar.</summary>
    public static readonly AuthorizationDenial ActorTypeNotDeclared = new(
        "Authz.ActorTypeNotDeclared",
        AuthorizationDenialReasons.NotDeclared,
        "This endpoint does not declare which kinds of account may use it."
    );

    public static readonly AuthorizationDenial SurfaceNotAllowed = new(
        "Auth.SurfaceNotAllowed",
        AuthorizationDenialReasons.Surface,
        "This session can't be used for this action."
    );

    /// <summary>Deja la denegación para el result handler. La capa 3 no puede escribir la respuesta:
    /// la policy solo devuelve true/false y el 403 lo produce el middleware después.</summary>
    public void Record(HttpContext context) => context.Items[HttpContextItemKey] = this;

    public static AuthorizationDenial? Recorded(HttpContext context) =>
        context.Items.TryGetValue(HttpContextItemKey, out var recorded) ? recorded as AuthorizationDenial : null;

    /// <summary>
    /// RFC 9457. Mantiene <c>code</c> y agrega <c>message</c> —el mismo par que ya devuelve
    /// <see cref="BuildingBlocks.Results.Error"/>— para que los frontends desplegados, que leen
    /// <c>code</c>/<c>message</c>, no se enteren del cambio. Antes el 403 de las capas 1 y 2 llegaba
    /// con el cuerpo vacío.
    /// </summary>
    public ProblemDetails ToProblemDetails(HttpContext context)
    {
        var problem = new ProblemDetails
        {
            Type = "https://taxvision.dev/problems/authorization",
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = Detail,
            Extensions =
            {
                ["code"] = Code,
                ["reason"] = Reason,
                ["message"] = Detail,
                ["correlationId"] =
                    context.Response.Headers[CorrelationIdMiddleware.Header].FirstOrDefault()
                    ?? context.Request.Headers[CorrelationIdMiddleware.Header].FirstOrDefault(),
            },
        };

        if (Module is not null)
            problem.Extensions["module"] = Module;
        if (Permission is not null)
            problem.Extensions["permission"] = Permission;

        return problem;
    }

    /// <summary>Escribe el 403 directo. Lo usan los filtros de las capas 1 y 2, que corren dentro de
    /// MVC y ya pueden producir el resultado.</summary>
    public async Task WriteAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(ToProblemDetails(context), ProblemJsonOptions),
            context.RequestAborted
        );
    }

    /// <summary>camelCase, igual que el resto de las respuestas del sistema.</summary>
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web);
}
