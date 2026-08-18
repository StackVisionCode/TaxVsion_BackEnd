namespace TaxVision.Tasks.Application.Backfill.Abstractions;

/// <summary>
/// Backfill del directorio de customers para un tenant recién descubierto. Nunca lanza: una falla de
/// red o HTTP se loguea y el tenant queda pendiente para el próximo evento.
/// </summary>
public interface ITenantCustomerBackfillService
{
    Task EnsureBackfilledAsync(Guid tenantId, CancellationToken ct = default);
}
