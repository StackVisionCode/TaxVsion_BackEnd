using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess;

/// <summary>
/// Capabilities efectivas computadas desde el scope del actor para un item. Todas true = allow;
/// deny per-item las apaga una por una. Delete/ShareExternal viajan en el payload por consistencia
/// con CRM — el portal no borra ni comparte, pero el UI los muestra como "deshabilitado por tu oficina".
/// </summary>
public sealed record EffectiveCapabilities(bool Preview, bool Download, bool Upload, bool Delete, bool ShareExternal)
{
    public static readonly EffectiveCapabilities AllowAll = new(true, true, true, true, true);

    public static EffectiveCapabilities ForFile(StorageActorScope scope, Guid fileId)
    {
        if (!scope.IsCustomerPortal)
            return AllowAll;
        return new EffectiveCapabilities(
            Preview: !scope.IsDeniedForPortal(AccessResourceType.File, fileId, AccessCapability.Preview),
            Download: !scope.IsDeniedForPortal(AccessResourceType.File, fileId, AccessCapability.Download),
            // Upload no aplica a un archivo; se deja true por consistencia (la decisión real vive en la carpeta).
            Upload: true,
            Delete: !scope.IsDeniedForPortal(AccessResourceType.File, fileId, AccessCapability.Delete),
            ShareExternal: !scope.IsDeniedForPortal(AccessResourceType.File, fileId, AccessCapability.ShareExternal)
        );
    }

    public static EffectiveCapabilities ForFolder(StorageActorScope scope, Guid folderId)
    {
        if (!scope.IsCustomerPortal)
            return AllowAll;
        return new EffectiveCapabilities(
            Preview: !scope.IsDeniedForPortal(AccessResourceType.Folder, folderId, AccessCapability.Preview),
            Download: !scope.IsDeniedForPortal(AccessResourceType.Folder, folderId, AccessCapability.Download),
            Upload: !scope.IsDeniedForPortal(AccessResourceType.Folder, folderId, AccessCapability.Upload),
            Delete: !scope.IsDeniedForPortal(AccessResourceType.Folder, folderId, AccessCapability.Delete),
            ShareExternal: !scope.IsDeniedForPortal(AccessResourceType.Folder, folderId, AccessCapability.ShareExternal)
        );
    }

    /// <summary>
    /// Baseline del portal del cliente — agrega SOLO las reglas tenant-scope (preset) del tenant.
    /// Lo usa el front como gate global cuando no hay folder activo (vista raíz "My documents"): sin
    /// esto el botón "Send documents" se pintaba aunque el preset dijera Upload deny.
    /// Devuelve null si el actor no es portal o no hay ninguna regla tenant-scope — mantiene el payload
    /// limpio y "sin ruido" para staff y para portales sin preset.
    /// </summary>
    public static EffectiveCapabilities? ForTenantBaseline(StorageActorScope scope)
    {
        if (!scope.IsCustomerPortal)
            return null;
        if (!scope.DenyRules.ContainsKey((AccessResourceType.TenantScope, scope.TenantId)))
            return null;
        return new EffectiveCapabilities(
            Preview: !scope.IsDeniedForPortal(AccessResourceType.TenantScope, scope.TenantId, AccessCapability.Preview),
            Download: !scope.IsDeniedForPortal(
                AccessResourceType.TenantScope,
                scope.TenantId,
                AccessCapability.Download
            ),
            Upload: !scope.IsDeniedForPortal(AccessResourceType.TenantScope, scope.TenantId, AccessCapability.Upload),
            Delete: !scope.IsDeniedForPortal(AccessResourceType.TenantScope, scope.TenantId, AccessCapability.Delete),
            ShareExternal: !scope.IsDeniedForPortal(
                AccessResourceType.TenantScope,
                scope.TenantId,
                AccessCapability.ShareExternal
            )
        );
    }

    /// <summary>
    /// Reason más representativo del deny visible en el response (Download para archivo, Upload para
    /// carpeta); null si no hay deny. Prefiere reason per-item, cae a tenant-scope si existe — la
    /// resolución la hace <see cref="StorageActorScope.DenyReasonFor"/>.
    /// </summary>
    public static string? ReasonForFile(StorageActorScope scope, Guid fileId) =>
        scope.DenyReasonFor(AccessResourceType.File, fileId, AccessCapability.Download)
        ?? scope.DenyReasonFor(AccessResourceType.File, fileId, AccessCapability.Delete)
        ?? scope.DenyReasonFor(AccessResourceType.File, fileId, AccessCapability.ShareExternal);

    public static string? ReasonForFolder(StorageActorScope scope, Guid folderId) =>
        scope.DenyReasonFor(AccessResourceType.Folder, folderId, AccessCapability.Upload)
        ?? scope.DenyReasonFor(AccessResourceType.Folder, folderId, AccessCapability.Download)
        ?? scope.DenyReasonFor(AccessResourceType.Folder, folderId, AccessCapability.Delete)
        ?? scope.DenyReasonFor(AccessResourceType.Folder, folderId, AccessCapability.ShareExternal);

    /// <summary>Razón de la regla tenant-scope (preset) para esa capability — expuesta por si el UI la quiere mostrar aparte.</summary>
    public static string? ReasonTenantScope(StorageActorScope scope, AccessCapability capability)
    {
        if (!scope.IsCustomerPortal)
            return null;
        if (!scope.DenyRules.TryGetValue((AccessResourceType.TenantScope, scope.TenantId), out var set))
            return null;
        return set.Where(r => r.Capability == capability)
            .OrderBy(r => r.SetAtUtc)
            .Select(r => r.Reason)
            .FirstOrDefault(r => r != null);
    }
}

/// <summary>Code estable del 403 por deny per-item: el portal lo mapea a un copy conocido.</summary>
public static class CustomerAccessDenied
{
    public static readonly BuildingBlocks.Results.Error Error = new(
        "cloudstorage.customer_access.denied",
        "This action was blocked by your tax office."
    );

    public static BuildingBlocks.Results.Error With(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? Error : new BuildingBlocks.Results.Error(Error.Code, reason);
}
