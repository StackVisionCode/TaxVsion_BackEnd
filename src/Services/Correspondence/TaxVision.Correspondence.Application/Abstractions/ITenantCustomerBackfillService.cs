namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Backfill de <c>CustomerEmailAddresses</c> (Fase 2) para un tenant recién descubierto — ver
/// <see cref="ITenantBackfillStateRepository"/> para la marca de "ya corrido" y
/// <see cref="ICorrespondenceCustomerClient"/> para la llamada M2M a Customer.Api. Nunca lanza:
/// una falla de red/HTTP se loguea y el tenant queda pendiente para el próximo evento.
/// </summary>
public interface ITenantCustomerBackfillService
{
    Task EnsureBackfilledAsync(Guid tenantId, CancellationToken ct = default);
}
