using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Abstractions;

public interface IConnectedMailboxRepository
{
    Task<ConnectedMailbox?> GetByAccountIdAsync(Guid tenantId, Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// El buzón de OFICINA activo más recientemente conectado (DM-02): nunca uno personal ni uno con dueño sin
    /// reconciliar. Si hay más de uno de oficina, la última conexión gana.
    /// </summary>
    Task<ConnectedMailbox?> FindActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Filas con dueño sin reconciliar (anteriores a la proyección del dueño), con seguimiento.</summary>
    Task<IReadOnlyList<ConnectedMailbox>> ListWithUnknownOwnershipAsync(int max, CancellationToken ct = default);

    Task AddAsync(ConnectedMailbox account, CancellationToken ct = default);
}
