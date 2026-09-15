using System.Text.Json;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;

namespace TaxVision.Auth.Application.Users.Queries;

/// <summary>Un permiso concedido, con el módulo al que mapea y si es efectivo. Efectivo = el permiso
/// no está gateado por módulo (transversal, <c>Module == null</c>) o su módulo está habilitado en el
/// plan del tenant. Un permiso concedido pero NO efectivo es el caso "tengo el permiso pero me da 403
/// porque el módulo no está en mi plan".</summary>
public sealed record EffectivePermissionAccess(string Code, string? Module, bool Effective);

/// <summary>Vista de debugging del acceso del usuario actual (Fase 7): actor type, roles, módulos
/// habilitados y, por permiso, su módulo y si es efectivo. Responde "por qué 403" sin leer logs.</summary>
public sealed record EffectiveAccessResponse(
    string ActorType,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<EffectivePermissionAccess> Permissions
);

public sealed record GetMyEffectiveAccessQuery(Guid UserId);

public static class GetMyEffectiveAccessHandler
{
    public static async Task<Result<EffectiveAccessResponse>> Handle(
        GetMyEffectiveAccessQuery query,
        IUserRepository users,
        IRoleRepository roles,
        ITenantPlanLimitsStore planLimits,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<EffectiveAccessResponse>(new Error("User.NotFound", "User does not exist."));

        var (roleNames, permissions) = await UserAccessResolver.ResolveAsync(user, roles, ct);

        var limits = await planLimits.GetAsync(user.TenantId, ct);
        var enabledModules =
            limits is null ? [] : JsonSerializer.Deserialize<List<string>>(limits.EnabledModulesJson) ?? [];
        var enabledSet = enabledModules.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var permissionAccess = permissions
            .Select(code =>
            {
                var module = PermissionModuleMap.ModuleFor(code);
                var effective = module is null || enabledSet.Contains(module);
                return new EffectivePermissionAccess(code, module, effective);
            })
            .ToList();

        return Result.Success(
            new EffectiveAccessResponse(user.ActorType.ToString(), roleNames, enabledModules, permissionAccess)
        );
    }
}
