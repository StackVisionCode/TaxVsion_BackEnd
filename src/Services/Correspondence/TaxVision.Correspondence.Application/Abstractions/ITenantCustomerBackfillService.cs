using Wolverine;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Backfill de <c>CustomerEmailAddresses</c> (Fase 2) para un tenant recién descubierto: si no está completo,
/// encola <c>BackfillTenantCustomersCommand</c>. No llama a Customer.Api (COR-08).
/// </summary>
public interface ITenantCustomerBackfillService
{
    /// <param name="bus">El del handler: el comando sale junto con su transacción.</param>
    Task EnsureBackfilledAsync(Guid tenantId, IMessageBus bus, CancellationToken ct = default);
}
