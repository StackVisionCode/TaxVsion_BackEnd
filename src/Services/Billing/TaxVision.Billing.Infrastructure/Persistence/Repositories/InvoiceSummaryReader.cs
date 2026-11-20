using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>
/// Totales de la oficina: una sola consulta con lo mínimo de cada factura. IgnoreQueryFilters + tenant explícito (scope
/// de Wolverine sin tenant ambiental) y el mismo filtro de asignación que <see cref="InvoiceRepository"/>. El Money se
/// guarda como texto ("cents|CUR"), así que la suma se hace en memoria, no en SQL.
/// </summary>
public sealed class InvoiceSummaryReader(BillingDbContext dbContext) : IInvoiceSummaryReader
{
    public async Task<IReadOnlyList<InvoiceSummaryRow>> ListAsync(
        Guid tenantId,
        Guid? assignedToUserId,
        CancellationToken ct = default
    )
    {
        var query = dbContext
            .Invoices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.DeletedAtUtc == null);

        if (assignedToUserId is { } assignee)
            query = query.Where(i =>
                dbContext
                    .CustomerAssignmentProjections.IgnoreQueryFilters()
                    .Any(a => a.TenantId == tenantId && a.CustomerId == i.CustomerId && a.UserId == assignee)
            );

        var rows = await query
            .Select(i => new
            {
                i.Status,
                i.Currency,
                i.AmountDue,
                i.AmountPaid,
                i.CreatedAtUtc,
                i.PaidAtUtc,
            })
            .ToListAsync(ct);

        return rows.Select(r => new InvoiceSummaryRow(
                r.Status,
                r.Currency,
                r.AmountDue.AmountCents,
                r.AmountPaid.AmountCents,
                r.CreatedAtUtc,
                r.PaidAtUtc
            ))
            .ToList();
    }
}
