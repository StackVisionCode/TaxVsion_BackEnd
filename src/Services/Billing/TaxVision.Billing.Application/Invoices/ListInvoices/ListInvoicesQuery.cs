using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GetInvoice;
using TaxVision.Billing.Application.Invoices.GetInvoicesDashboardSummary;

namespace TaxVision.Billing.Application.Invoices.ListInvoices;

public sealed record ListInvoicesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    int Take = 50,
    Guid? CustomerId = null,
    InvoiceListStatus? Status = null
);

/// <summary>Filtros del dashboard: "open" y "past-due" son las facturas del aging.</summary>
public enum InvoiceListStatus
{
    Open,
    PastDue,
}

public static class InvoiceListStatuses
{
    /// <summary>"open" o "past-due"; cualquier otro valor se ignora (null).</summary>
    public static InvoiceListStatus? Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "open" => InvoiceListStatus.Open,
            "past-due" => InvoiceListStatus.PastDue,
            _ => null,
        };
}

public static class ListInvoicesHandler
{
    /// <summary>Tope del listado: la UI pide 200 como máximo; un take enorme no vuelca toda la oficina.</summary>
    public const int MaxTake = 500;

    public static async Task<Result<IReadOnlyList<InvoiceSummaryResponse>>> Handle(
        ListInvoicesQuery query,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
        IOfficeTimeZone zones,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        // Filtro por asignación solo si el flag está ON y el actor no ve todo (view_all/admin).
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var list = await invoices.ListByTenantAsync(
            query.TenantId,
            Math.Min(query.Take, MaxTake),
            ct,
            assignedTo,
            query.CustomerId,
            await OpenFilterAsync(query, zones, clock, ct)
        );

        IReadOnlyList<InvoiceSummaryResponse> response = list.Select(invoice => new InvoiceSummaryResponse(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.Status.ToString(),
                invoice.Currency,
                invoice.Subtotal.AmountCents,
                invoice.TaxTotal.AmountCents,
                invoice.Total.AmountCents,
                invoice.AmountDue.AmountCents,
                invoice.AmountPaid.AmountCents,
                invoice.PdfFileId,
                invoice.CreatedAtUtc,
                invoice.PaidAtUtc,
                invoice.PaymentMethod?.ToString(),
                invoice.ReceiptNumber,
                invoice.ReceiptHash,
                invoice.ActivePaymentLink?.CheckoutUrl,
                invoice.CustomerId
            ))
            .ToList();

        return Result.Success(response);
    }

    // Vencida = fecha base anterior a hoy en la zona de la oficina, el mismo corte del aging.
    private static async Task<OpenInvoicesFilter?> OpenFilterAsync(
        ListInvoicesQuery query,
        IOfficeTimeZone zones,
        TimeProvider clock,
        CancellationToken ct
    ) =>
        query.Status switch
        {
            InvoiceListStatus.Open => new OpenInvoicesFilter(),
            InvoiceListStatus.PastDue => new OpenInvoicesFilter(
                InvoiceDashboardPeriods.PastDueBeforeUtc(
                    clock.GetUtcNow().UtcDateTime,
                    await zones.ResolveAsync(query.TenantId, ct)
                )
            ),
            _ => null,
        };
}
