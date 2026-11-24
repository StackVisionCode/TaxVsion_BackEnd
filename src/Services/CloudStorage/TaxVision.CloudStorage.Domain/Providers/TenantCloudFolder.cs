using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.Providers;

/// <summary>
/// Carpeta ya creada en un proveedor que trabaja por id (Google Drive): ruta legible → id remoto. Evita buscarla o
/// crearla en cada subida; si el usuario la borra, se olvida y se vuelve a crear.
/// </summary>
public sealed class TenantCloudFolder : TenantEntity
{
    private TenantCloudFolder() { }

    public const int PathMaxLength = 512;
    public const int RemoteFolderIdMaxLength = 256;

    public string Provider { get; private set; } = string.Empty;

    /// <summary>Ruta desde la raíz del proveedor, p. ej. <c>TaxOffix/Documents/2025</c>.</summary>
    public string Path { get; private set; } = string.Empty;

    public string RemoteFolderId { get; private set; } = string.Empty;

    public static TenantCloudFolder Create(Guid tenantId, string provider, string path, string remoteFolderId)
    {
        var folder = new TenantCloudFolder
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Path = path,
            RemoteFolderId = remoteFolderId,
        };
        folder.SetTenant(tenantId);
        return folder;
    }
}
