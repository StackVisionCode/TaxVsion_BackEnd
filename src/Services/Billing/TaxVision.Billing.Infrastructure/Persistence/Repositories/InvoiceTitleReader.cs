using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>
/// Títulos en vivo: IgnoreQueryFilters + tenant explícito (scope de Wolverine sin tenant ambiental) y el mismo
/// filtro de asignación que <see cref="InvoiceRepository"/>. Una sola consulta para todo el lote.
/// </summary>
public sealed class InvoiceTitleReader(BillingDbContext dbContext) : IInvoiceTitleReader
{
    public async Task<IReadOnlyList<InvoiceTitleRow>> ListAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> invoiceIds,
        Guid? assignedToUserId,
        CancellationToken ct = default
    )
    {
        var ids = invoiceIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var query = dbContext
            .Invoices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.DeletedAtUtc == null && ids.Contains(i.Id));

        if (assignedToUserId is { } assignee)
            query = query.Where(i =>
                dbContext
                    .CustomerAssignmentProjections.IgnoreQueryFilters()
                    .Any(a => a.TenantId == tenantId && a.CustomerId == i.CustomerId && a.UserId == assignee)
            );

        return await query.Select(i => new InvoiceTitleRow(i.Id, i.InvoiceNumber)).ToListAsync(ct);
    }
}
