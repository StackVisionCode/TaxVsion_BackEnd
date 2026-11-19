using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Application.Invoices.WorkTitles;

/// <summary>Títulos en vivo de las facturas que el actor puede leer (mismo ABAC que GET de facturas).</summary>
public sealed record GetInvoiceWorkTitlesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    IReadOnlyList<Guid> Ids
);

public static class GetInvoiceWorkTitlesHandler
{
    public static async Task<WorkTitlesResponse> Handle(
        GetInvoiceWorkTitlesQuery query,
        IInvoiceTitleReader invoices,
        IOptions<BillingVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var rows = await invoices.ListAsync(query.TenantId, query.Ids, assignedTo, ct);

        // Solo el número, nunca montos; un borrador sin número no tiene título.
        return new WorkTitlesResponse(
            rows.Where(row => !string.IsNullOrWhiteSpace(row.InvoiceNumber))
                .Select(row => (row.Id, Title: WorkLimits.ClipTitle($"Invoice {row.InvoiceNumber}")))
                .Where(row => row.Title is not null)
                .Select(row => new WorkTitle(WorkResourceTypes.Invoice, row.Id, row.Title!))
                .ToList()
        );
    }
}
