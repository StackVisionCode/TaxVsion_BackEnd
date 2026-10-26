namespace TaxVision.Notification.Application.Directory.Abstractions;

/// <summary>Escritura del backfill desde el snapshot de Customer: nunca pisa una fila más nueva.</summary>
public interface ICustomerContactPolicySnapshotWriter
{
    /// <summary>Actualiza solo si la fila es anterior al snapshot. Devuelve si la actualizó.</summary>
    Task<bool> ReconcileIfOlderAsync(
        Guid tenantId,
        Guid customerId,
        bool emailEnabled,
        bool smsEnabled,
        string language,
        DateTime snapshotAtUtc,
        CancellationToken ct = default
    );
}
