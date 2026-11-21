using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.RedeclareFileSources;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>Backfill: Billing re-declara el PDF vigente y el pagado de cada factura de la oficina.</summary>
public sealed class RedeclareFileSourcesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid CustomerA = Guid.NewGuid();
    private static readonly Guid CustomerB = Guid.NewGuid();

    [Fact]
    public async Task DryRun_CountsWithoutPublishing()
    {
        var (reader, rows) = Reader();
        var publisher = new RecordingPublisher();

        var report = await RunAsync(reader, publisher, dryRun: true);

        Assert.True(report.DryRun);
        Assert.Equal(rows.Count, report.ResourcesScanned);
        Assert.Equal(4, report.FilesDeclared);
        Assert.Empty(publisher.Pages);
    }

    [Fact]
    public async Task Run_PublishesOneDeclarationPerDistinctPdf_AsInvoicePdf()
    {
        var (reader, rows) = Reader();
        var publisher = new RecordingPublisher();

        var report = await RunAsync(reader, publisher, dryRun: false);

        var declared = publisher.Pages.SelectMany(page => page).Cast<FileSourceDeclaredIntegrationEvent>().ToList();
        Assert.False(report.DryRun);
        Assert.Equal(4, report.FilesDeclared);
        Assert.Equal(4, declared.Count);
        Assert.All(
            declared,
            d =>
            {
                Assert.Equal("billing", d.SourceService);
                Assert.Equal("invoice", d.SourceResourceType);
                Assert.Equal("invoice_pdf", d.SourceRole);
                Assert.Equal(Tenant, d.TenantId);
            }
        );
        var paid = rows[1];
        Assert.Contains(declared, d => d.FileId == paid.PaidPdfFileId && d.SourceResourceId == paid.InvoiceId);
        Assert.Contains(declared, d => d.FileId == paid.PdfFileId && d.RelatedCustomerId == CustomerB);
    }

    [Fact]
    public async Task Run_PublishesEachPageInItsOwnUnit()
    {
        var (reader, _) = Reader();
        var publisher = new RecordingPublisher();

        await RunAsync(reader, publisher, dryRun: false, pageSize: 2);

        Assert.Equal([3, 1], publisher.Pages.Select(page => page.Count));
    }

    [Fact]
    public async Task Repeating_PublishesTheSameDeclarations()
    {
        var (reader, _) = Reader();
        var first = new RecordingPublisher();
        var second = new RecordingPublisher();

        await RunAsync(reader, first, dryRun: false);
        await RunAsync(reader, second, dryRun: false);

        Assert.Equal(Keys(first), Keys(second));
    }

    private static IEnumerable<string> Keys(RecordingPublisher publisher) =>
        publisher
            .Pages.SelectMany(page => page)
            .Cast<FileSourceDeclaredIntegrationEvent>()
            .Select(d => $"{d.FileId}:{d.SourceResourceId}:{d.SourceRole}:{d.RelatedCustomerId}")
            .Order();

    // Tres facturas: una con PDF igual al pagado, otra con PDF y pagado distintos, otra con solo el PDF.
    private static (FakeReader Reader, List<InvoicePdfFileRow> Rows) Reader()
    {
        var same = Guid.NewGuid();
        List<InvoicePdfFileRow> rows =
        [
            new(Guid.NewGuid(), CustomerA, same, same),
            new(Guid.NewGuid(), CustomerB, Guid.NewGuid(), Guid.NewGuid()),
            new(Guid.NewGuid(), CustomerA, Guid.NewGuid(), null),
        ];
        return (new FakeReader(rows), rows);
    }

    private static async Task<RedeclaredFileSourcesReport> RunAsync(
        FakeReader reader,
        RecordingPublisher publisher,
        bool dryRun,
        int pageSize = RedeclareFileSourcesHandler.PageSize
    )
    {
        var result = await RedeclareFileSourcesHandler.RunAsync(
            new RedeclareFileSourcesCommand(Tenant, dryRun),
            reader,
            publisher,
            new NoopCorrelationContext(),
            TimeProvider.System,
            pageSize,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class FakeReader(List<InvoicePdfFileRow> rows) : IInvoicePdfFileReader
    {
        public Task<IReadOnlyList<InvoicePdfFileRow>> ListPageAsync(
            Guid tenantId,
            int skip,
            int take,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<InvoicePdfFileRow>>(rows.Skip(skip).Take(take).ToList());

        public Task<IReadOnlyList<InvoicePdfFileRow>> ListByFileIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> fileIds,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class RecordingPublisher : IIntegrationEventPagePublisher
    {
        public List<IReadOnlyList<IIntegrationEvent>> Pages { get; } = [];

        public Task PublishPageAsync(IReadOnlyList<IIntegrationEvent> events, CancellationToken ct = default)
        {
            Pages.Add(events);
            return Task.CompletedTask;
        }
    }
}
