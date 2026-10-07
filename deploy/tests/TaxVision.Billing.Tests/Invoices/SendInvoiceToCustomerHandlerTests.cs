using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.SendInvoiceToCustomer;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Wolverine;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// Mandar la factura por correo es del backend: publica el evento con los datos y Notification la
/// renderiza con la plantilla de Scribe. Antes el CRM componía el HTML en el navegador y lo mandaba
/// al endpoint genérico de correo, así que el cuerpo del correo lo decidía el cliente.
/// </summary>
public sealed class SendInvoiceToCustomerHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task An_issued_invoice_publishes_what_the_template_needs()
    {
        var invoice = Issued("client@example.com");
        invoice.AttachPaymentLink(Guid.NewGuid(), "https://manfer.taxproffice.com/payments-client/invoices/abc", Now);
        var bus = new FakeMessageBus();

        var result = await HandleAsync(invoice, bus);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<InvoiceSentToCustomerIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("INV-2026-00005", evt.InvoiceNumber);
        Assert.Equal("client@example.com", evt.CustomerEmail);
        Assert.Equal("https://manfer.taxproffice.com/payments-client/invoices/abc", evt.PaymentLink);
        Assert.Equal(10000, evt.AmountDueCents);
    }

    [Fact]
    public async Task A_draft_is_not_sent()
    {
        var bus = new FakeMessageBus();

        var result = await HandleAsync(Draft("client@example.com"), bus);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.NotIssued", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_client_without_email_is_rejected_before_publishing()
    {
        // El navegador ya avisaba de esto, pero el endpoint no puede confiar en que lo haya hecho:
        // sin esta guarda el evento viaja y muere en Notification, donde nadie lo ve.
        var bus = new FakeMessageBus();

        var result = await HandleAsync(Issued(email: null), bus);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.CustomerHasNoEmail", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task The_assignment_scope_is_the_same_as_the_other_writes()
    {
        var invoices = new RecordingInvoices(null);

        await SendInvoiceToCustomerHandler.Handle(
            new SendInvoiceToCustomerCommand(Tenant, Guid.NewGuid(), Actor, CanViewAll: false),
            invoices,
            Options.Create(new BillingVisibilityOptions { Enabled = true }),
            new NoOpCorrelation(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal(Actor, invoices.AskedFor);
    }

    private static async Task<BuildingBlocks.Results.Result> HandleAsync(Invoice invoice, FakeMessageBus bus) =>
        await SendInvoiceToCustomerHandler.Handle(
            new SendInvoiceToCustomerCommand(Tenant, invoice.Id, Actor, CanViewAll: true),
            new RecordingInvoices(invoice),
            Options.Create(new BillingVisibilityOptions { Enabled = true }),
            new NoOpCorrelation(),
            bus,
            CancellationToken.None
        );

    private static Invoice Draft(string? email)
    {
        var result = Invoice.CreateDraft(
            tenantId: Tenant,
            actorUserId: Actor,
            customer: new CustomerSnapshot(Guid.NewGuid(), "Client", email, null, null, null),
            currency: "USD",
            lines: [new DraftInvoiceLine("Tax prep", 1, 10000, 0)],
            notes: null,
            nowUtc: Now
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static Invoice Issued(string? email)
    {
        var invoice = Draft(email);
        Assert.True(invoice.Issue("INV-2026-00005", Now, Now.AddDays(30), Actor).IsSuccess);
        return invoice;
    }

    private sealed class RecordingInvoices(Invoice? invoice) : IInvoiceRepository
    {
        public Guid? AskedFor { get; private set; }

        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        )
        {
            AskedFor = assignedToUserId;
            return Task.FromResult(invoice);
        }

        public Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Invoice>> ListByTenantAsync(
            Guid tenantId,
            int take,
            CancellationToken ct = default,
            Guid? assignedToUserId = null,
            Guid? customerId = null
        ) => throw new NotSupportedException();

        public Task AddAsync(Invoice invoice, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoOpCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test-correlation";

        public IDisposable Push(string correlationId) => new Noop();

        public void Set(string correlationId) { }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
