using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;
using TaxVision.Billing.Application.Invoices.EditInvoice;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Application;

/// <summary>Editar una factura emitida no llama a PaymentClient ni descuenta stock en línea (BIL-3): el
/// handler ya no recibe el cliente de cobro y deja el enlace a EnsureInvoicePaymentLinkCommand.</summary>
public sealed class EditInvoiceHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly Guid CatalogItemId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly RecordingInventoryClient _inventory = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();

    [Fact]
    public async Task EditIssued_DoesNotCallPaymentClientInline()
    {
        var invoice = IssuedInvoice();

        var result = await EditAsync(invoice, quantity: 5);

        Assert.True(result.IsSuccess);
        Assert.Single(_bus.Published.OfType<EnsureInvoicePaymentLinkCommand>());
        Assert.Empty(_bus.Published.OfType<GenerateInvoicePdfCommand>());
    }

    [Fact]
    public async Task EditIssued_ChecksStockAndPublishesSync_WithoutCommitting()
    {
        var invoice = IssuedInvoice();

        var result = await EditAsync(invoice, quantity: 5);

        Assert.True(result.IsSuccess);
        Assert.Empty(_inventory.Commits);
        var check = Assert.Single(_inventory.Checks);
        Assert.Equal(5, Assert.Single(check.Lines).Quantity);
        var sync = Assert.Single(_bus.Published.OfType<SyncInvoiceStockCommand>());
        Assert.Equal(invoice.Id, sync.InvoiceId);
    }

    [Fact]
    public async Task EditIssued_InsufficientStock_SavesNothing()
    {
        var invoice = IssuedInvoice();
        _inventory.CheckResult = Result.Failure(new Error("inventory.insufficientStock", "No stock."));

        var result = await EditAsync(invoice, quantity: 5);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _unitOfWork.Saves);
        Assert.Empty(_bus.Published);
        Assert.Empty(_inventory.Commits);
    }

    [Fact]
    public async Task EditDraft_ChecksNoStock_AndPublishesNothing()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Ink cartridge", 2, 2_000, 0, CatalogItemId)],
                null,
                Now
            )
            .Value;

        var result = await EditAsync(invoice, quantity: 5);

        Assert.True(result.IsSuccess);
        Assert.Equal("Draft", result.Value.Status);
        Assert.Empty(_inventory.Checks);
        Assert.Equal(1, _unitOfWork.Saves);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task EditIssued_ChecksStockOnlyForCatalogLines()
    {
        var invoice = IssuedInvoice();

        var result = await EditInvoiceHandler.Handle(
            new EditInvoiceCommand(
                TenantId,
                invoice.Id,
                ActorUserId,
                new InvoiceCustomerInput(invoice.Customer.CustomerId, "Client", "client@example.com", null, null, null),
                "USD",
                [
                    new InvoiceLineInput("Ink cartridge", 3, 2_000, 0, CatalogItemId),
                    new InvoiceLineInput("Service", 1, 500, 0, null),
                    new InvoiceLineInput("Empty catalog", 1, 500, 0, Guid.Empty),
                ],
                null,
                CanViewAll: true
            ),
            new InMemoryInvoiceRepository(invoice),
            Options.Create(new BillingVisibilityOptions()),
            _inventory,
            _unitOfWork,
            _bus,
            new FixedClock(Now),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var line = Assert.Single(Assert.Single(_inventory.Checks).Lines);
        Assert.Equal(CatalogItemId, line.CatalogItemId);
        Assert.Equal(3, line.Quantity);
    }

    [Fact]
    public async Task EditVoided_IsRejected_WithoutSavingOrPublishing()
    {
        var invoice = IssuedInvoice();
        Assert.True(invoice.Void("wrong", Now, ActorUserId).IsSuccess);

        var result = await EditAsync(invoice, quantity: 5);

        Assert.Equal("Billing.Invoice.NotEditable", result.Error.Code);
        Assert.Empty(_inventory.Checks);
        Assert.Equal(0, _unitOfWork.Saves);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task EditPaidInvoiceUp_EnsuresLinkForDue()
    {
        var invoice = IssuedInvoice();
        Pay(invoice, 4_000);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        var result = await EditAsync(invoice, quantity: 5);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(InvoiceStatus.PartiallyPaid), result.Value.Status);
        Assert.Equal(6_000, invoice.AmountDue.AmountCents);
        Assert.Single(_bus.Published.OfType<EnsureInvoicePaymentLinkCommand>());
        Assert.Empty(Emitted(invoice).OfType<InvoicePaidIntegrationEvent>());
    }

    [Fact]
    public async Task EditPartiallyPaid_LowerTotal_BecomesPaid_PublishesInvoicePaid()
    {
        var invoice = IssuedInvoice();
        Pay(invoice, 2_000);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);

        var result = await EditAsync(invoice, quantity: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(InvoiceStatus.Paid), result.Value.Status);
        var emitted = Emitted(invoice);
        var paid = Assert.Single(emitted.OfType<InvoicePaidIntegrationEvent>());
        Assert.Empty(_bus.Published.OfType<InvoicePaidIntegrationEvent>());
        // El PDF lo encadena EnsureInvoicePaymentLink; no se pide otro.
        Assert.Empty(emitted.OfType<GenerateInvoicePdfCommand>());
        Assert.Equal(invoice.Id, paid.InvoiceId);
    }

    [Fact]
    public async Task EditPaid_StillPaid_DoesNotRepublishInvoicePaid()
    {
        var invoice = IssuedInvoice();
        Pay(invoice, 4_000);

        await EditAsync(invoice, quantity: 2);

        Assert.Empty(Emitted(invoice).OfType<InvoicePaidIntegrationEvent>());
    }

    private static void Pay(Invoice invoice, long cents)
    {
        Assert.True(
            invoice
                .ApplyPayment(
                    InvoicePaymentSource.Manual,
                    Guid.NewGuid().ToString(),
                    Money.Create(cents, "USD").Value,
                    PaymentMethod.Cash,
                    Now,
                    ActorUserId
                )
                .IsSuccess
        );
        // Lo sembrado ya está guardado: sus eventos ya salieron.
        invoice.ClearDomainEvents();
    }

    // Lo publicado a mano más lo que el commit despacharía por la cola domain-events.
    private IReadOnlyList<object> Emitted(Invoice invoice) =>
        [.. _bus.Published, .. DomainEventReplay.Dispatch([invoice])];

    private Task<Result<EditInvoiceResult>> EditAsync(Invoice invoice, int quantity) =>
        EditInvoiceHandler.Handle(
            new EditInvoiceCommand(
                TenantId,
                invoice.Id,
                ActorUserId,
                new InvoiceCustomerInput(invoice.Customer.CustomerId, "Client", "client@example.com", null, null, null),
                "USD",
                [new InvoiceLineInput("Ink cartridge", quantity, 2_000, 0, CatalogItemId)],
                null,
                CanViewAll: true
            ),
            new InMemoryInvoiceRepository(invoice),
            Options.Create(new BillingVisibilityOptions()),
            _inventory,
            _unitOfWork,
            _bus,
            new FixedClock(Now),
            CancellationToken.None
        );

    private static Invoice IssuedInvoice()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Ink cartridge", 2, 2_000, 0, CatalogItemId)],
                null,
                Now
            )
            .Value;
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        return invoice;
    }
}
