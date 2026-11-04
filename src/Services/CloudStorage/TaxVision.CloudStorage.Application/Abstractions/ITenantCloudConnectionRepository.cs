using TaxVision.CloudStorage.Domain.Providers;

namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>Conexiones de nube (Drive/OneDrive/Dropbox) de la oficina: a lo sumo una por proveedor.</summary>
public interface ITenantCloudConnectionRepository
{
    /// <summary>Todas las del tenant, tracked (el retiro las marca para revisión). Son pocas: una por proveedor.</summary>
    Task<IReadOnlyList<TenantCloudConnection>> ListByTenantAsync(Guid tenantId, CancellationToken ct);

    /// <summary>
    /// Marca para borrar (sin guardar) los <c>state</c> OAuth sin consumir que inició el usuario: un retirado
    /// no debe poder terminar una conexión empezada antes del retiro. Devuelve cuántos.
    /// </summary>
    Task<int> DiscardOAuthStatesInitiatedByAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
