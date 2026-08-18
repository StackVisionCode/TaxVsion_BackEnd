using System.Globalization;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.IssueInvoice;

public static class IssueInvoiceHandler
{
    private const int DefaultNetDays = 30;

    public static async Task<Result<IssueInvoiceResult>> Handle(
        IssueInvoiceCommand command,
        IInvoiceRepository invoices,
        IInvoiceNumberSequenceRepository sequences,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<IssueInvoiceResult>(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var periodKey = nowUtc.Year.ToString(CultureInfo.InvariantCulture);

        var sequence = await sequences.GetOrCreateAsync(command.TenantId, periodKey, ct);
        var number = sequence.Allocate();
        var invoiceNumber = $"INV-{periodKey}-{number:D5}";

        var issueResult = invoice.Issue(invoiceNumber, nowUtc, nowUtc.AddDays(DefaultNetDays), command.ActorUserId);
        if (issueResult.IsFailure)
            return Result.Failure<IssueInvoiceResult>(issueResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        // Post-commit (outbox durable): asegurar el ancla de cobro y LUEGO generar el PDF, cada paso en
        // su propia transacción con retry propio (punto 7 del review). Sellar el tenant para que
        // LocalCommandTenantMiddleware lo restaure.
        bus.TenantId = command.TenantId.ToString();
        await bus.PublishAsync(new EnsureInvoicePaymentLinkCommand(command.TenantId, command.InvoiceId));

        return Result.Success(new IssueInvoiceResult(invoice.Id, invoiceNumber, invoice.Status.ToString()));
    }
}
