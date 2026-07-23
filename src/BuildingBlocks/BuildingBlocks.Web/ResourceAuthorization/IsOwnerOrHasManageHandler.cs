using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace BuildingBlocks.ResourceAuthorization;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — Layer 3b (resource ownership), complementa a
/// <c>[HasPermission]</c> (capacidad) + <c>[AllowActorTypes]</c> (tipo de actor) + tenant boundary.
/// Reglas, en orden:
/// <list type="number">
/// <item>PlatformAdmin siempre pasa.</item>
/// <item>Si se configuró un permiso "manage" de override para este tipo de recurso y el actor lo
/// tiene, pasa (permite a un TenantAdmin operar sobre recursos de otros usuarios — SIEMPRE dentro
/// del mismo tenant, ver nota de tenant boundary abajo).</item>
/// <item>El creador del recurso (<see cref="IHasOwner.CreatedByUserId"/> == userId del JWT) pasa.</item>
/// <item>Todo lo demás falla (fail-closed — <see cref="AuthorizationHandlerContext.Fail()"/> no se
/// llama explícito porque no succeed-ear ya deniega la policy; ASP.NET Core solo necesita que NINGÚN
/// handler haga Succeed).</item>
/// </list>
/// <b>Tenant boundary:</b> este handler NO valida tenant — solo ownership. La garantía de "solo
/// dentro de su propio tenant" no vive acá sino en la resolución del recurso ANTES de llamar a
/// <c>AuthorizeAsync</c>: cada controller carga el recurso con
/// <c>repo.GetAsync(tenantId, resourceId, ct)</c> usando el <c>tenant_id</c> del JWT del actor que
/// llama, así que un recurso de otro tenant nunca se resuelve (404 antes de llegar acá) — el
/// "manage" override jamás ve un recurso ajeno al tenant del actor.
/// Genérico sobre <typeparamref name="TResource"/> — una instancia distinta se registra por cada
/// tipo de aggregate (<c>IsOwnerOrHasManageHandler&lt;ShareLink&gt;</c>,
/// <c>IsOwnerOrHasManageHandler&lt;SignatureRequest&gt;</c>, etc.) vía
/// <see cref="ResourceAuthorizationServiceCollectionExtensions.AddOwnershipAuthorization{TResource}"/>,
/// cada una con su propio permiso "manage" (o ninguno, si el recurso no tiene override).
/// RBAC Fase 7.5: el chequeo de "manage" pasa por <see cref="IUserPermissionsSource"/> en vez de leer
/// el claim <c>perm</c> directo del JWT — antes quedaba fuera del mecanismo compartido y hubiera
/// dejado de funcionar en silencio el día que el claim se sacara del token humano.
/// </summary>
public sealed class IsOwnerOrHasManageHandler<TResource>
    : AuthorizationHandler<OperationAuthorizationRequirement, TResource>
    where TResource : IHasOwner
{
    private readonly string? _managePermission;
    private readonly IUserPermissionsSource _permissionsSource;
    private readonly AuthorizationMetrics _metrics;

    public IsOwnerOrHasManageHandler(
        string? managePermission,
        IUserPermissionsSource permissionsSource,
        AuthorizationMetrics metrics
    )
    {
        _managePermission = managePermission;
        _permissionsSource = permissionsSource;
        _metrics = metrics;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        TResource resource
    )
    {
        var user = context.User;

        if (user.IsPlatformAdmin())
        {
            _metrics.RecordDecision(allowed: true, "3b");
            context.Succeed(requirement);
            return;
        }

        if (
            _managePermission is not null
            && await _permissionsSource.HasPermissionAsync(user, _managePermission, CancellationToken.None)
        )
        {
            _metrics.RecordDecision(allowed: true, "3b");
            context.Succeed(requirement);
            return;
        }

        if (user.TryGetUserId(out var userId) && userId == resource.CreatedByUserId)
        {
            _metrics.RecordDecision(allowed: true, "3b");
            context.Succeed(requirement);
            return;
        }

        _metrics.RecordDecision(allowed: false, "3b");
    }
}
