using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: Billing declara solo los PDF de sus facturas, con los
/// mismos valores que el backfill manual, e ignora el resto.
/// </summary>
public sealed class FileSourcesClaimRequestedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Declares_only_the_claimed_pdfs_as_invoice_pdf()
    {
        var paid = new InvoicePdfFileRow(Guid.NewGuid(), Customer, Guid.NewGuid(), Guid.NewGuid());
        var unclaimedPaid = new InvoicePdfFileRow(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid());
        var reader = new FakeReader((Tenant, paid), (Tenant, unclaimedPaid));
        var foreign = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await HandleAsync(
            Claim(Tenant, paid.PdfFileId!.Value, paid.PaidPdfFileId!.Value, unclaimedPaid.PdfFileId!.Value, foreign),
            reader,
            bus
        );

        var declared = bus.Published.Cast<FileSourceDeclaredIntegrationEvent>().ToList();
        Assert.Equal(3, declared.Count);
        Assert.All(
            declared,
            d =>
            {
                Assert.Equal(Tenant, d.TenantId);
                Assert.Equal("billing", d.SourceService);
                Assert.Equal("invoice", d.SourceResourceType);
                Assert.Equal("invoice_pdf", d.SourceRole);
                Assert.Equal(Now, d.OccurredOn);
            }
        );
        Assert.Contains(declared, d => d.FileId == paid.PdfFileId && d.SourceResourceId == paid.InvoiceId);
        Assert.Contains(declared, d => d.FileId == paid.PaidPdfFileId && d.RelatedCustomerId == Customer);
        var single = Assert.Single(declared, d => d.SourceResourceId == unclaimedPaid.InvoiceId);
        Assert.Equal(unclaimedPaid.PdfFileId, single.FileId);
        Assert.Null(single.RelatedCustomerId);
        Assert.DoesNotContain(declared, d => d.FileId == foreign);
    }

    [Fact]
    public async Task Ids_that_are_not_its_own_publish_nothing()
    {
        var reader = new FakeReader((Tenant, new InvoicePdfFileRow(Guid.NewGuid(), Customer, Guid.NewGuid(), null)));
        var bus = new FakeMessageBus();

        await HandleAsync(Claim(Tenant, Guid.NewGuid(), Guid.NewGuid()), reader, bus);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Empty_list_or_empty_tenant_does_nothing()
    {
        var row = new InvoicePdfFileRow(Guid.NewGuid(), Customer, Guid.NewGuid(), null);
        var reader = new FakeReader((Tenant, row));
        var bus = new FakeMessageBus();

        await HandleAsync(Claim(Tenant), reader, bus);
        await HandleAsync(Claim(Guid.Empty, row.PdfFileId!.Value), reader, bus);

        Assert.Empty(bus.Published);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task Only_looks_in_the_tenant_of_the_claim()
    {
        var row = new InvoicePdfFileRow(Guid.NewGuid(), Customer, Guid.NewGuid(), null);
        var reader = new FakeReader((OtherTenant, row));
        var bus = new FakeMessageBus();

        await HandleAsync(Claim(Tenant, row.PdfFileId!.Value), reader, bus);

        Assert.Empty(bus.Published);
        Assert.Equal(Tenant, Assert.Single(reader.Calls));
    }

    private static FileSourcesClaimRequestedIntegrationEvent Claim(Guid tenantId, params Guid[] fileIds) =>
        new() { TenantId = tenantId, FileIds = fileIds };

    private static Task HandleAsync(
        FileSourcesClaimRequestedIntegrationEvent claim,
        FakeReader reader,
        FakeMessageBus bus
    ) =>
        FileSourcesClaimRequestedConsumer.Handle(
            claim,
            reader,
            bus,
            new NoopCorrelationContext(),
            new FixedClock(Now),
            CancellationToken.None
        );

    // Simula la consulta: solo el tenant pedido y filas con algún PDF en la lista.
    private sealed class FakeReader(params (Guid TenantId, InvoicePdfFileRow Row)[] rows) : IInvoicePdfFileReader
    {
        public List<Guid> Calls { get; } = [];

        public Task<IReadOnlyList<InvoicePdfFileRow>> ListPageAsync(
            Guid tenantId,
            int skip,
            int take,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<InvoicePdfFileRow>> ListByFileIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> fileIds,
            CancellationToken ct = default
        )
        {
            Calls.Add(tenantId);
            return Task.FromResult<IReadOnlyList<InvoicePdfFileRow>>(
                rows.Where(r =>
                        r.TenantId == tenantId
                        && (
                            r.Row.PdfFileId is { } pdf && fileIds.Contains(pdf)
                            || r.Row.PaidPdfFileId is { } paid && fileIds.Contains(paid)
                        )
                    )
                    .Select(r => r.Row)
                    .ToList()
            );
        }
    }
}
