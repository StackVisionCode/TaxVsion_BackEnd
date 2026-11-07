using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Application.Invoices.VoidInvoice;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>BB-14: por InvokeAsync, lo publicado antes de un guardado fallido sale igual tras el reintento en línea.</summary>
public sealed class VoidInvoiceHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Void_WhenSaveFails_PublishesNothing()
    {
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<TimeoutException>(() => RunAsync(IssuedInvoice(), new FailingUnitOfWork(), bus));

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Void_WhenSaved_PublishesInTheSameOrder()
    {
        var bus = new FakeMessageBus();

        var result = await RunAsync(IssuedInvoice(), new CountingUnitOfWork(), bus);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            bus.Published,
            message => Assert.IsType<InvoiceVoidedIntegrationEvent>(message),
            message => Assert.IsType<GenerateInvoicePdfCommand>(message),
            message => Assert.IsType<SyncInvoiceStockCommand>(message)
        );
    }

    private static Task<Result> RunAsync(Invoice invoice, IUnitOfWork unitOfWork, FakeMessageBus bus) =>
        VoidInvoiceHandler.Handle(
            new VoidInvoiceCommand(TenantId, invoice.Id, ActorUserId, "wrong client", CanViewAll: true),
            new SingleInvoice(invoice),
            Options.Create(new BillingVisibilityOptions()),
            unitOfWork,
            bus,
            new NoopCorrelationContext(),
            TimeProvider.System,
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
                [new DraftInvoiceLine("Tax preparation", 1, 10_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue("INV-2026-00001", Now, Now.AddDays(30), ActorUserId).IsSuccess);
        return invoice;
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated SQL timeout.");
    }

    private sealed class SingleInvoice(Invoice invoice) : IInvoiceRepository
    {
        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => Task.FromResult<Invoice?>(invoice);

        public Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Invoice?> GetOnboardingInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default) =>
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
}
