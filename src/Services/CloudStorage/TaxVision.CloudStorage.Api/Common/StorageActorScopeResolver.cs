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
    ICustomerAccessRuleRepository accessRules,
    IOptions<CloudStorageSourceReadGateOptions> sourceGate,
    IOptions<CloudStorageOwnerMediatedGateOptions> ownerGate
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
        // Servicio: su actorId no es una persona; ni asignaciones ni compuertas.
        if (baseScope.IsService)
            return baseScope;
        // Persona (personal, PlatformAdmin o portal): lo que media su dueño no existe por id ni en listados.
        var person = ownerGate.Value.Enabled ? baseScope with { HidesOwnerMediated = true } : baseScope;
        // Portal: el aislamiento por CustomerId ya cubre el acceso; el filtro ABAC no aplica. Las reglas
        // per-item deny se precargan UNA sola vez para el portal cuando su flag está on.
        if (person.IsCustomerPortal)
            return perItem.Value.Enabled ? await WithPortalDenyRulesAsync(tenantId, person, ct) : person;

        var scope = sourceGate.Value.Enabled ? await WithReadableSurfacesAsync(user, person, ct) : person;
        // Flag off = comportamiento previo intacto (bypass).
        if (!visibility.Value.Enabled)
            return scope;
        // customers.view_all = bypass intencional de la oficina/administrador.
        if (await permissions.HasPermissionAsync(user, CustomersPermissions.ViewAll, ct))
            return scope;

        var assignedCustomerIds = await assignments.GetAssignedCustomerIdsAsync(tenantId, actorId, ct);
        return scope with { BypassAssignmentFilter = false, AssignedCustomerIds = assignedCustomerIds };
    }

    private async Task<StorageActorScope> WithReadableSurfacesAsync(
        ClaimsPrincipal user,
        StorageActorScope baseScope,
        CancellationToken ct
    )
    {
        var readable = new HashSet<FileReadSurface>();
        foreach (var surface in FileReadGate.All)
        foreach (var permission in FileReadGate.PermissionsOf(surface))
        {
            if (!await permissions.HasPermissionAsync(user, permission, ct))
                continue;
            readable.Add(surface);
            break;
        }
        return baseScope with { ReadableSurfaces = readable };
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
