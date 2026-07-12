using TaxVision.Customer.Domain.Audit;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Puerto de escritura del audit trail de Customer — mismo criterio que IAuthAuditWriter
/// en Auth (seam de DI explícito en vez de un `.Add()` directo contra el DbContext).
/// </summary>
public interface ICustomerAuditWriter
{
    Task AddAsync(CustomerAuditLog log, CancellationToken ct);
}
