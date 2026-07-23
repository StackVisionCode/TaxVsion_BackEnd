using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Capa 2 del plan de autorización por actor type (ver Actor_Type_Authorization_Layers_Plan.md,
/// sección 5) — corre para TODA acción de todo controller una vez registrado (ver
/// <see cref="ActorTypeAuthorizationExtensions.AddActorTypeAuthorization"/>). Fail-closed: si la
/// acción no declara <see cref="AllowActorTypesAttribute"/> (ni en el método ni en el
/// controller), se bloquea con 403 — nunca se abre por default. Los endpoints
/// <c>[AllowAnonymous]</c> se saltean (no hay actor autenticado que validar — la anonimidad ya la
/// decidió el propio endpoint). Las marcadas con <see cref="AuthorizedByCapabilityTokenAttribute"/>
/// también se saltean — su propia policy de Capa 3 (<c>[Authorize(Policy = "...")]</c>, aplicada
/// por el middleware <c>UseAuthorization()</c> ANTES de que corra este filtro) ya autoriza al
/// portador de un token sin identidad persistente (ver el atributo para el criterio exacto de
/// cuándo aplica). <see cref="ActorType.PlatformAdmin"/> siempre pasa, igual que ya hace
/// <see cref="ClaimsPrincipalExtensions.HasPermission"/> hoy.
/// </summary>
public sealed class ActorTypeAuthorizationFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
            return;

        if (IsAnonymous(descriptor) || IsAuthorizedByCapabilityToken(descriptor))
            return;

        var declared = ResolveDeclaredActorTypes(descriptor);
        if (declared is null)
        {
            context.HttpContext.RequestServices.GetRequiredService<AuthorizationMetrics>().RecordDecision(false, "2");
            context.Result = new ForbidResult();
            return;
        }

        var allowed = IsActorAllowed(context.HttpContext.User.GetActorType(), declared);
        context.HttpContext.RequestServices.GetRequiredService<AuthorizationMetrics>().RecordDecision(allowed, "2");
        if (!allowed)
            context.Result = new ForbidResult();
    }

    private static bool IsAnonymous(ControllerActionDescriptor descriptor) =>
        descriptor.MethodInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0
        || descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0;

    private static bool IsAuthorizedByCapabilityToken(ControllerActionDescriptor descriptor) =>
        descriptor.MethodInfo.GetCustomAttributes(typeof(AuthorizedByCapabilityTokenAttribute), inherit: true).Length
            > 0
        || descriptor
            .ControllerTypeInfo.GetCustomAttributes(typeof(AuthorizedByCapabilityTokenAttribute), inherit: true)
            .Length > 0;

    private static AllowActorTypesAttribute? ResolveDeclaredActorTypes(ControllerActionDescriptor descriptor) =>
        descriptor
            .MethodInfo.GetCustomAttributes(typeof(AllowActorTypesAttribute), inherit: true)
            .Cast<AllowActorTypesAttribute>()
            .FirstOrDefault()
        ?? descriptor
            .ControllerTypeInfo.GetCustomAttributes(typeof(AllowActorTypesAttribute), inherit: true)
            .Cast<AllowActorTypesAttribute>()
            .FirstOrDefault();

    private static bool IsActorAllowed(ActorType? actorType, AllowActorTypesAttribute declared) =>
        actorType == ActorType.PlatformAdmin
        || (actorType is not null && declared.ActorTypes.Contains(actorType.Value));
}

public static class ActorTypeAuthorizationExtensions
{
    /// <summary>
    /// Registra <see cref="ActorTypeAuthorizationFilter"/> globalmente. Cada microservicio lo
    /// activa una vez desde su Program.cs (<c>builder.Services.AddControllers().AddActorTypeAuthorization()</c>)
    /// — Fase 3/4 del plan. No hace nada hasta que se llama explícitamente; agregar este único
    /// paquete acá no cambia el comportamiento de ningún servicio todavía.
    /// </summary>
    public static IMvcBuilder AddActorTypeAuthorization(this IMvcBuilder builder)
    {
        // RBAC Fase 10: AuthorizationMetrics es singleton (Meter propio) — registrado acá porque
        // los 14 servicios ya llaman este método una vez desde su Program.cs, así PermissionPolicyProvider
        // (Layer 1) e IsOwnerOrHasManageHandler (Layer 3b, cuando aplica) lo resuelven sin wiring extra.
        builder.Services.AddSingleton<AuthorizationMetrics>();
        return builder.AddMvcOptions(options => options.Filters.Add<ActorTypeAuthorizationFilter>());
    }
}
