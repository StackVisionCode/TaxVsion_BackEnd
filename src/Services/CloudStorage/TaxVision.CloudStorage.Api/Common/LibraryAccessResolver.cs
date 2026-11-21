using System.Security.Claims;
using BuildingBlocks.Authorization;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Library;

namespace TaxVision.CloudStorage.Api.Common;

/// <summary>Resuelve qué orígenes de la biblioteca lee quien llama. Vive en Api porque usa el ClaimsPrincipal.</summary>
public interface ILibraryAccessResolver
{
    Task<LibraryAccess> ResolveAsync(ClaimsPrincipal user, Guid tenantId, Guid actorId, CancellationToken ct);
}

/// <summary>
/// Origen legible = alguno de sus permisos de lectura y su módulo habilitado (siempre, sin depender del escalón del
/// gate). <c>Auth.TokenStale</c> no se captura: el middleware lo convierte en 401.
/// </summary>
public sealed class LibraryAccessResolver(
    IUserPermissionsSource permissions,
    ITenantModuleEntitlementsSource modules,
    ICustomerAssignmentProjectionStore assignments,
    IOptions<CloudStorageVisibilityOptions> visibility
) : ILibraryAccessResolver
{
    public async Task<LibraryAccess> ResolveAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        Guid actorId,
        CancellationToken ct
    )
    {
        var readable = new List<LibrarySource>();
        var variants = new Dictionary<LibrarySource, IReadOnlyList<string>>();
        foreach (var definition in LibrarySourceCatalog.Published)
        {
            if (definition.Variants is null)
            {
                if (await CanReadAsync(user, definition.ReadPermissions, definition.Module, ct))
                    readable.Add(definition.Source);
                continue;
            }
            // Origen con variantes: aparece con una sola cumplida.
            var met = await ReadableVariantsAsync(user, definition, ct);
            if (met.Count == 0)
                continue;
            readable.Add(definition.Source);
            variants[definition.Source] = met;
        }

        var canDownload = await permissions.HasPermissionAsync(user, CloudStoragePermissions.FileDownload, ct);
        var viewAll = await permissions.HasPermissionAsync(user, CustomersPermissions.ViewAll, ct);
        // financial se recorta siempre: los asignados se cargan aunque el flag esté apagado.
        IReadOnlyCollection<Guid> assigned = viewAll
            ? []
            : await assignments.GetAssignedCustomerIdsAsync(tenantId, actorId, ct);
        return new LibraryAccess(readable, canDownload, viewAll, visibility.Value.Enabled, assigned, variants);
    }

    /// <summary>Ids de las variantes que quien llama cumple (permiso y módulo de cada una).</summary>
    public async Task<IReadOnlyList<string>> ReadableVariantsAsync(
        ClaimsPrincipal user,
        LibrarySourceDefinition definition,
        CancellationToken ct
    )
    {
        var met = new List<string>();
        foreach (var variant in definition.Variants ?? [])
            if (await CanReadAsync(user, variant.ReadPermissions, variant.Module, ct))
                met.Add(variant.Id);
        return met;
    }

    private async Task<bool> CanReadAsync(
        ClaimsPrincipal user,
        IReadOnlyList<string> readPermissions,
        string? module,
        CancellationToken ct
    )
    {
        var hasPermission = false;
        foreach (var permission in readPermissions)
            if (await permissions.HasPermissionAsync(user, permission, ct))
            {
                hasPermission = true;
                break;
            }
        return hasPermission && (module is null || await modules.IsModuleEnabledAsync(user, module, ct));
    }
}
