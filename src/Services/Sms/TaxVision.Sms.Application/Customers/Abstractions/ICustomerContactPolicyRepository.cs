using TaxVision.Sms.Domain.Customers;

namespace TaxVision.Sms.Application.Customers.Abstractions;

/// <summary>Proyección local de la política de contacto del cliente (canal SMS).</summary>
public interface ICustomerContactPolicyRepository
{
    Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    );

    /// <summary>De los clientes dados, los que tienen los SMS apagados. Una sola consulta por lote.</summary>
    Task<IReadOnlyCollection<Guid>> GetSmsDisabledCustomerIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    );

    /// <summary>De los clientes dados, los que ya tienen fila. Una sola consulta por lote.</summary>
    Task<IReadOnlyCollection<Guid>> GetExistingCustomerIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    );

    Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default);

    /// <summary>Backfill: actualiza solo si la fila es anterior al snapshot. Devuelve si la actualizó.</summary>
    Task<bool> ReconcileIfOlderAsync(
        Guid tenantId,
        Guid customerId,
        bool smsEnabled,
        DateTime snapshotAtUtc,
        CancellationToken ct = default
    );
}
