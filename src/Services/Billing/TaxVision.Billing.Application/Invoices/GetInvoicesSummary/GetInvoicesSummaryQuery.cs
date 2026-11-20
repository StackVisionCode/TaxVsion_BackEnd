using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Invoices.GetInvoicesSummary;

/// <summary>
/// Totales de facturación de la oficina (las tarjetas de cabecera). Es información del negocio: la exige su propio
/// permiso (<c>invoicing.summary.view</c>), que el owner delega; ver facturas (<c>invoicing.view</c>) no lo trae.
/// </summary>
public sealed record GetInvoicesSummaryQuery(Guid TenantId, Guid ActorUserId, bool CanViewAll);

/// <summary><paramref name="AverageDaysToPay"/> es null mientras no se cobró ninguna factura.</summary>
public sealed record InvoicesSummaryResponse(
    string? Currency,
    long CollectedCents,
    long OutstandingCents,
    int DraftCount,
    double? AverageDaysToPay,
    int InvoiceCount
);

public static class GetInvoicesSummaryHandler
{
    public static async Task<Result<InvoicesSummaryResponse>> Handle(
        GetInvoicesSummaryQuery query,
        IInvoiceSummaryReader invoices,
        IOptions<BillingVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        // Misma visibilidad por asignación que la tabla: quien no ve todo, totaliza solo lo suyo.
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var rows = await invoices.ListAsync(query.TenantId, assignedTo, ct);
        return Result.Success(Summarize(rows));
    }

    /// <summary>Los importes no se mezclan entre monedas: se totaliza la moneda de la factura más reciente.</summary>
    public static InvoicesSummaryResponse Summarize(IReadOnlyList<InvoiceSummaryRow> rows)
    {
        if (rows.Count == 0)
            return new InvoicesSummaryResponse(null, 0, 0, 0, null, 0);

        var currency = rows.OrderByDescending(r => r.CreatedAtUtc).First().Currency;
        var inCurrency = rows.Where(r => string.Equals(r.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Un borrador todavía no debe nada (no se emitió) y una anulada tampoco.
        var outstanding = inCurrency
            .Where(r => r.Status is not (InvoiceStatus.Draft or InvoiceStatus.Voided))
            .Sum(r => r.AmountDueCents);
        var collected = inCurrency.Sum(r => r.AmountPaidCents);

        var paidDays = rows.Where(r => r.PaidAtUtc is not null)
            .Select(r => (r.PaidAtUtc!.Value - r.CreatedAtUtc).TotalDays)
            .ToList();
        double? average = paidDays.Count == 0 ? null : Math.Round(paidDays.Average(), 1);

        return new InvoicesSummaryResponse(
            currency,
            collected,
            outstanding,
            rows.Count(r => r.Status == InvoiceStatus.Draft),
            average,
            rows.Count
        );
    }
}
