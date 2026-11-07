namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Backfill de <c>CustomerEmailAddresses</c> (Fase 2) para un tenant recién descubierto: si no está completo,
/// encola <c>BackfillTenantCustomersCommand</c>. No llama a Customer.Api (COR-08).
/// </summary>
public interface ITenantCustomerBackfillService
{
    Task EnsureBackfilledAsync(Guid tenantId, CancellationToken ct = default);
}
