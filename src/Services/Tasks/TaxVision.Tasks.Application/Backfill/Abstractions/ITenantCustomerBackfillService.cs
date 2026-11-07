namespace TaxVision.Tasks.Application.Backfill.Abstractions;

/// <summary>
/// Backfill del directorio de customers para un tenant recién descubierto. Nunca lanza: una falla de
/// red o HTTP se loguea y el tenant queda pendiente para el próximo evento.
/// </summary>
public interface ITenantCustomerBackfillService
{
    /// <summary>Solo mira el estado local: no llama a Customer.</summary>
    Task<bool> NeedsBackfillAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Pagina contra Customer y guarda por página. No se llama con una transacción abierta.</summary>
    Task BackfillAsync(Guid tenantId, CancellationToken ct = default);
}
