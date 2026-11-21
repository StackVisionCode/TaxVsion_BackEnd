using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;

namespace TaxVision.Billing.Application.Invoices.RedeclareFileSources;

/// <summary>Resultado del backfill: facturas miradas y archivos declarados (o por declarar, con DryRun).</summary>
public sealed record RedeclaredFileSourcesReport(bool DryRun, int ResourcesScanned, int FilesDeclared);

/// <summary>
/// Backfill (una vez por oficina): re-declara en CloudStorage el PdfFileId y el PaidPdfFileId de cada factura
/// como <c>invoice_pdf</c>. Con <paramref name="DryRun"/> solo cuenta. Idempotente: repetirlo no cambia nada.
/// </summary>
public sealed record RedeclareFileSourcesCommand(Guid TenantId, bool DryRun);

public static class RedeclareFileSourcesHandler
{
    public const int PageSize = 500;

    public static Task<Result<RedeclaredFileSourcesReport>> Handle(
        RedeclareFileSourcesCommand command,
        IInvoicePdfFileReader reader,
        IIntegrationEventPagePublisher publisher,
        ICorrelationContext correlation,
        TimeProvider clock,
        CancellationToken ct
    ) => RunAsync(command, reader, publisher, correlation, clock, PageSize, ct);

    /// <summary>Una página por transacción con outbox; no modifica filas de Billing.</summary>
    public static async Task<Result<RedeclaredFileSourcesReport>> RunAsync(
        RedeclareFileSourcesCommand command,
        IInvoicePdfFileReader reader,
        IIntegrationEventPagePublisher publisher,
        ICorrelationContext correlation,
        TimeProvider clock,
        int pageSize,
        CancellationToken ct
    )
    {
        int scanned = 0,
            declared = 0;
        while (true)
        {
            var page = await reader.ListPageAsync(command.TenantId, scanned, pageSize, ct);
            if (page.Count == 0)
                break;

            var events = Declarations(command.TenantId, page, clock, correlation.CorrelationId);
            if (!command.DryRun && events.Count > 0)
                await publisher.PublishPageAsync(events, ct);

            scanned += page.Count;
            declared += events.Count;
            if (page.Count < pageSize)
                break;
        }

        return Result.Success(new RedeclaredFileSourcesReport(command.DryRun, scanned, declared));
    }

    private static List<IIntegrationEvent> Declarations(
        Guid tenantId,
        IReadOnlyList<InvoicePdfFileRow> page,
        TimeProvider clock,
        string correlationId
    )
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        return page.SelectMany(row => InvoicePdfSource.DeclareCurrent(tenantId, row, nowUtc, correlationId))
            .Cast<IIntegrationEvent>()
            .ToList();
    }
}
