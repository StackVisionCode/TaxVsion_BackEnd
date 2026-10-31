using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>Applies the same CRM storage mode to any registered cloud provider adapter.</summary>
public sealed class StoragePolicyDispatcher(
    CloudStorageDbContext db,
    IEnumerable<IStoragePolicyProviderAdapter> adapters
)
{
    public async Task CopyAvailableFileAsync(FileAvailableIntegrationEvent message, CancellationToken ct)
    {
        // Fotos de perfil y pistas de grabación no se espejan al Drive/OneDrive/Dropbox de la oficina:
        // allá el borrado (foto reemplazada, retención de pistas) nunca llegaría.
        if (
            string.Equals(message.FolderType, nameof(FolderType.Avatars), StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.FolderType, nameof(FolderType.RecordingTracks), StringComparison.OrdinalIgnoreCase)
        )
            return;

        var policy = await db
            .StoragePolicies.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == message.TenantId, ct);
        if (policy is null || policy.Mode is not ("primary" or "both"))
            return;
        await Resolve(policy.ProviderId).CopyAvailableFileAsync(message, ct);
    }

    public async Task BackupTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var policy = await db
            .StoragePolicies.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (policy is null || policy.Mode != "backup")
            return;
        await Resolve(policy.ProviderId).BackupTenantAsync(tenantId, ct);
    }

    private IStoragePolicyProviderAdapter Resolve(string providerId) =>
        adapters.FirstOrDefault(adapter =>
            string.Equals(adapter.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
        )
        ?? throw new InvalidOperationException($"No storage policy adapter is registered for provider '{providerId}'.");
}
