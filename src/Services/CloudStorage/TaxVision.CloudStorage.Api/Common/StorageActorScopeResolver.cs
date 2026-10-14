using System.Security.Claims;
using BuildingBlocks.Authorization;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Api.Common;

/// <summary>
/// Enriquece el <see cref="StorageActorScope"/> con el set de clientes asignados al actor para las queries
/// de listado (visibilidad por asignación). Mimético del fetch de <c>customers.view_all</c> que el
/// CustomerController hace antes de cada lectura. El resultado se pasa al bus — el handler nunca necesita
/// consultar la proyección en su propio scope de Wolverine.
/// </summary>
public interface IStorageActorScopeResolver
{
    Task<StorageActorScope> ResolveAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        Guid actorId,
        StorageActorScope baseScope,
        CancellationToken ct
    );
}

public sealed class StorageActorScopeResolver(
    ICustomerAssignmentProjectionStore assignments,
    IUserPermissionsSource permissions,
    IOptions<CloudStorageVisibilityOptions> visibility,
    IOptions<CloudStoragePerItemAccessOptions> perItem,
    ICustomerAccessRuleRepository accessRules
) : IStorageActorScopeResolver
{
    public async Task<StorageActorScope> ResolveAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        Guid actorId,
        StorageActorScope baseScope,
        CancellationToken ct
    )
    {
        // Portal: el aislamiento por CustomerId ya cubre el acceso; el filtro ABAC no aplica. Las reglas
        // per-item deny se precargan UNA sola vez para el portal cuando su flag está on.
        if (baseScope.IsCustomerPortal)
            return perItem.Value.Enabled ? await WithPortalDenyRulesAsync(tenantId, baseScope, ct) : baseScope;
        // Flag off = comportamiento previo intacto (bypass).
        if (!visibility.Value.Enabled)
            return baseScope;
        // customers.view_all = bypass intencional de la oficina/administrador.
        if (await permissions.HasPermissionAsync(user, CustomersPermissions.ViewAll, ct))
            return baseScope;

        var assignedCustomerIds = await assignments.GetAssignedCustomerIdsAsync(tenantId, actorId, ct);
        return baseScope with { BypassAssignmentFilter = false, AssignedCustomerIds = assignedCustomerIds };
    }

    /// <summary>Precarga todas las reglas deny del cliente del portal agrupadas por (ResourceType, ResourceId).</summary>
    private async Task<StorageActorScope> WithPortalDenyRulesAsync(
        Guid tenantId,
        StorageActorScope baseScope,
        CancellationToken ct
    )
    {
        var customerId = baseScope.CustomerId;
        if (customerId is null)
            return baseScope;

        var rules = await accessRules.ListByCustomerAsync(tenantId, customerId.Value, ct);
        if (rules.Count == 0)
            return baseScope;

        var grouped = rules
            .GroupBy(r => (r.ResourceType, r.ResourceId))
            .ToDictionary(
                g => g.Key,
                g =>
                    (IReadOnlyCollection<CustomerAccessRuleView>)g.Select(r => CustomerAccessRuleMapper.Map(r)).ToList()
            );
        return baseScope with { DenyRules = grouped };
    }
}
