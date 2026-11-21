using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GetInvoicesSummary;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// Totales de la oficina (tarjetas de cabecera): se calculan sobre TODAS las facturas visibles, con la misma
/// visibilidad por asignación que la tabla.
/// </summary>
public sealed class GetInvoicesSummaryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Day = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Totals_skip_drafts_and_voided_for_outstanding_and_count_every_payment()
    {
        var summary = GetInvoicesSummaryHandler.Summarize([
            Row(InvoiceStatus.Issued, due: 10_000, paid: 0),
            Row(InvoiceStatus.PartiallyPaid, due: 4_000, paid: 6_000),
            Row(InvoiceStatus.Paid, due: 0, paid: 15_000, paidAfterDays: 10),
            Row(InvoiceStatus.Draft, due: 99_000, paid: 0),
            Row(InvoiceStatus.Voided, due: 77_000, paid: 0),
        ]);

        Assert.Equal("USD", summary.Currency);
        Assert.Equal(14_000, summary.OutstandingCents);
        Assert.Equal(21_000, summary.CollectedCents);
        Assert.Equal(1, summary.DraftCount);
        Assert.Equal(10, summary.AverageDaysToPay);
        Assert.Equal(5, summary.InvoiceCount);
    }

    [Fact]
    public void No_invoices_is_an_empty_summary_without_an_average()
    {
        var summary = GetInvoicesSummaryHandler.Summarize([]);

        Assert.Null(summary.Currency);
        Assert.Equal(0, summary.OutstandingCents);
        Assert.Equal(0, summary.CollectedCents);
        Assert.Null(summary.AverageDaysToPay);
    }

    [Fact]
    public void Amounts_in_another_currency_are_not_mixed_into_the_totals()
    {
        var summary = GetInvoicesSummaryHandler.Summarize([
            Row(InvoiceStatus.Issued, due: 10_000, paid: 0, createdOffsetDays: 5),
            Row(InvoiceStatus.Issued, due: 50_000, paid: 0, currency: "DOP", createdOffsetDays: 1),
        ]);

        // Manda la moneda de la factura más reciente.
        Assert.Equal("USD", summary.Currency);
        Assert.Equal(10_000, summary.OutstandingCents);
    }

    [Fact]
    public async Task A_restricted_actor_totals_only_their_assigned_clients()
    {
        var reader = new CapturingReader();

        await GetInvoicesSummaryHandler.Handle(
            new GetInvoicesSummaryQuery(TenantId, ActorUserId, CanViewAll: false),
            reader,
            Options.Create(new BillingVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

        Assert.Equal(ActorUserId, reader.AssignedTo);
    }

    [Fact]
    public async Task An_actor_who_sees_everything_totals_the_whole_office()
    {
        var reader = new CapturingReader();

        await GetInvoicesSummaryHandler.Handle(
            new GetInvoicesSummaryQuery(TenantId, ActorUserId, CanViewAll: true),
            reader,
            Options.Create(new BillingVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

        Assert.Null(reader.AssignedTo);
    }

    private static InvoiceSummaryRow Row(
        InvoiceStatus status,
        long due,
        long paid,
        int? paidAfterDays = null,
        string currency = "USD",
        int createdOffsetDays = 0
    )
    {
        var created = Day.AddDays(createdOffsetDays);
        return new InvoiceSummaryRow(
            status,
            currency,
            due,
            paid,
            created,
            paidAfterDays is { } days ? created.AddDays(days) : null
        );
    }

    private sealed class CapturingReader : IInvoiceSummaryReader
    {
        public Guid? AssignedTo { get; private set; } = Guid.Empty;

        public Task<IReadOnlyList<InvoiceSummaryRow>> ListAsync(
            Guid tenantId,
            Guid? assignedToUserId,
            CancellationToken ct = default
        )
        {
            AssignedTo = assignedToUserId;
            return Task.FromResult<IReadOnlyList<InvoiceSummaryRow>>([]);
        }
    }
}
