using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.ResyncInvoiceArtifacts;

/// <summary>
/// Reposición (BIL-BB2): vuelve a pedir el enlace de cobro y, encadenado, el PDF de una factura emitida que quedó
/// sin ellos. Idempotente: el payable es único por factura y la clave del PDF es determinista.
/// </summary>
public sealed record ResyncInvoiceArtifactsCommand(Guid TenantId, Guid InvoiceId);

public static class ResyncInvoiceArtifactsHandler
{
    public static async Task<Result> Handle(
        ResyncInvoiceArtifactsCommand command,
        IInvoiceRepository invoices,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));
        if (invoice.InvoiceNumber is null)
            return Result.Failure(new Error("Billing.Invoice.NotIssued", "Only an issued invoice has payment link and PDF."));

        // Cola local durable: sale con el commit del handler.
        bus.TenantId = command.TenantId.ToString();
        await bus.PublishAsync(new EnsureInvoicePaymentLinkCommand(command.TenantId, command.InvoiceId));
        return Result.Success();
    }
}
