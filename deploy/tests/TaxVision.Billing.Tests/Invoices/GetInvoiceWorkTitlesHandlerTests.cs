using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.WorkTitles;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>Títulos en vivo de facturas: "Invoice {número}", sin montos; sin número no hay título.</summary>
public sealed class GetInvoiceWorkTitlesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public async Task Title_is_the_invoice_number_and_drafts_without_number_are_omitted()
    {
        var (issued, draft) = (Guid.NewGuid(), Guid.NewGuid());
        var reader = new RecordingReader([
            new InvoiceTitleRow(issued, "INV-2026-00042"),
            new InvoiceTitleRow(draft, null),
        ]);

        var response = await Handle(reader, [issued, draft]);

        Assert.Equal([new WorkTitle(WorkResourceTypes.Invoice, issued, "Invoice INV-2026-00042")], response.Items);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Assignment_filter_applies_only_with_the_flag_and_without_customers_view_all(
        bool flagEnabled,
        bool canViewAll,
        bool expectRestricted
    )
    {
        var reader = new RecordingReader([]);

        await Handle(reader, [Guid.NewGuid()], flagEnabled, canViewAll);

        Assert.Equal(TenantId, reader.LastTenantId);
        Assert.Equal(expectRestricted ? ActorId : null, reader.LastAssignedTo);
    }

    // ----- helpers -----

    private static Task<WorkTitlesResponse> Handle(
        RecordingReader reader,
        IReadOnlyList<Guid> ids,
        bool flagEnabled = false,
        bool canViewAll = true
    ) =>
        GetInvoiceWorkTitlesHandler.Handle(
            new GetInvoiceWorkTitlesQuery(TenantId, ActorId, canViewAll, ids),
            reader,
            Options.Create(new BillingVisibilityOptions { Enabled = flagEnabled }),
            CancellationToken.None
        );

    private sealed class RecordingReader(IReadOnlyList<InvoiceTitleRow> rows) : IInvoiceTitleReader
    {
        public Guid? LastTenantId { get; private set; }
        public Guid? LastAssignedTo { get; private set; }

        public Task<IReadOnlyList<InvoiceTitleRow>> ListAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> invoiceIds,
            Guid? assignedToUserId,
            CancellationToken ct = default
        )
        {
            (LastTenantId, LastAssignedTo) = (tenantId, assignedToUserId);
            return Task.FromResult(rows);
        }
    }
}
