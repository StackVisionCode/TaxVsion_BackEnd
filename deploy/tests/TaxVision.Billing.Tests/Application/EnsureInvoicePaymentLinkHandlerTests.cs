using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Application;

public sealed class EnsureInvoicePaymentLinkHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PartiallyPaidReplacement_RequestsAmountDue()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.LinkAsReplacementFor(Guid.NewGuid(), 10_000, Now).IsSuccess);
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00002").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        Assert.True(invoice.ApplyCarriedCredit(Now).IsSuccess);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        var links = new RecordingPaymentLinkClient();

        await EnsureInvoicePaymentLinkHandler.Handle(
            new EnsureInvoicePaymentLinkCommand(TenantId, invoice.Id),
            new SingleInvoiceRepository(invoice),
            links,
            new CountingUnitOfWork(),
            new FakeMessageBus(),
            new FixedClock(Now),
            CancellationToken.None
        );

        Assert.Equal(5_000, Assert.Single(links.RequestedAmounts));
    }

    [Fact]
    public async Task ConfirmedLink_MarksTheDueThatWasSent()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00003").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        var unitOfWork = new CountingUnitOfWork();

        await EnsureInvoicePaymentLinkHandler.Handle(
            new EnsureInvoicePaymentLinkCommand(TenantId, invoice.Id),
            new SingleInvoiceRepository(invoice),
            new RecordingPaymentLinkClient(),
            unitOfWork,
            new FakeMessageBus(),
            new FixedClock(Now),
            CancellationToken.None
        );

        Assert.Equal(15_000, invoice.PaymentLinkSyncedDue?.AmountCents);
        Assert.Equal(1, unitOfWork.Saves);
    }

    // Una orden tardía (o reintentada) no puede dejar cobrable una factura que volvió a borrador.
    [Fact]
    public async Task A_numbered_draft_is_not_made_payable()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00004").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        Assert.True(invoice.OverrideStatus(InvoiceStatus.Draft, "Office correction", Now, ActorUserId).IsSuccess);
        var links = new RecordingPaymentLinkClient();

        await EnsureInvoicePaymentLinkHandler.Handle(
            new EnsureInvoicePaymentLinkCommand(TenantId, invoice.Id, Reinstate: true),
            new SingleInvoiceRepository(invoice),
            links,
            new CountingUnitOfWork(),
            new FakeMessageBus(),
            new FixedClock(Now),
            CancellationToken.None
        );

        Assert.Empty(links.RequestedAmounts);
        Assert.Null(invoice.ActivePaymentLink);
    }

    private sealed class RecordingPaymentLinkClient : IInvoicePaymentLinkClient
    {
        public List<long> RequestedAmounts { get; } = [];
        public List<bool> ReinstateRequests { get; } = [];

        public Task<Result<InvoicePayableResult>> EnsurePayableAsync(
            long amountCents,
            string currency,
            Guid invoiceId,
            Guid tenantId,
            string? invoiceNumber = null,
            CancellationToken ct = default,
            bool reinstate = false
        )
        {
            RequestedAmounts.Add(amountCents);
            ReinstateRequests.Add(reinstate);
            return Task.FromResult(
                Result.Success(new InvoicePayableResult(Guid.NewGuid(), "https://pay.example.test/i/1"))
            );
        }
    }

    private sealed class SingleInvoiceRepository(Invoice invoice) : IInvoiceRepository
    {
        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => Task.FromResult<Invoice?>(invoice.Id == invoiceId ? invoice : null);

        public Task<IReadOnlyList<Invoice>> ListByTenantAsync(
            Guid tenantId,
            int take,
            CancellationToken ct = default,
            Guid? assignedToUserId = null,
            Guid? customerId = null,
            OpenInvoicesFilter? open = null
        ) => Task.FromResult<IReadOnlyList<Invoice>>([invoice]);

        public Task AddAsync(Invoice value, CancellationToken ct = default) => Task.CompletedTask;
    }
}
