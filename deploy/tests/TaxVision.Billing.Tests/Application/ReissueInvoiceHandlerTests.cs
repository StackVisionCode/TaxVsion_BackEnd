using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.ReissueInvoice;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Application;

/// <summary>Caracterización de la reemisión (BIL-13): montos, enlaces, mensajes y errores de hoy.</summary>
public sealed class ReissueInvoiceHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly Guid CatalogItemId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(3);

    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();

    [Fact]
    public async Task PaidInvoice_VoidsOriginal_AndCarriesWhatWasPaid()
    {
        var original = IssuedInvoice();
        Pay(original, 6_000);
        Pay(original, 4_000);
        var invoices = new InMemoryInvoiceRepository(original);

        var result = await ReissueAsync(invoices, original, "  wrong client  ");

        Assert.True(result.IsSuccess);
        var replacement = Assert.Single(invoices.Rows, i => i.Id != original.Id);
        Assert.Equal(replacement.Id, result.Value.ReplacementInvoiceId);
        Assert.Equal(original.Id, result.Value.OriginalInvoiceId);
        Assert.Equal("Draft", result.Value.ReplacementStatus);

        Assert.Equal(InvoiceStatus.Voided, original.Status);
        Assert.Equal("wrong client", original.VoidReason);
        Assert.Equal(Later, original.VoidedAtUtc);
        Assert.Equal(ActorUserId, original.LastModifiedBy);
        Assert.Equal(replacement.Id, original.ReplacedByInvoiceId);
        Assert.Equal(10_000, original.AmountPaid.AmountCents);

        Assert.Equal(InvoiceStatus.Draft, replacement.Status);
        Assert.Equal(original.Id, replacement.ReplacesInvoiceId);
        Assert.Equal(10_000, replacement.CarriedCreditCents);
        Assert.Equal(0, replacement.AmountPaid.AmountCents);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task PartiallyPaidInvoice_CarriesOnlyWhatWasPaid()
    {
        var original = IssuedInvoice();
        Pay(original, 2_500);
        var invoices = new InMemoryInvoiceRepository(original);

        await ReissueAsync(invoices, original, null);

        Assert.Equal(2_500, Assert.Single(invoices.Rows, i => i.Id != original.Id).CarriedCreditCents);
    }

    [Fact]
    public async Task UnpaidInvoice_CarriesNothing()
    {
        var original = IssuedInvoice();
        var invoices = new InMemoryInvoiceRepository(original);

        await ReissueAsync(invoices, original, null);

        Assert.Equal(0, Assert.Single(invoices.Rows, i => i.Id != original.Id).CarriedCreditCents);
    }

    [Fact]
    public async Task Overpaid_CarriesTheWholePayment()
    {
        var original = IssuedInvoice();
        Pay(original, 10_000);
        Pay(original, 1_500);
        var invoices = new InMemoryInvoiceRepository(original);

        await ReissueAsync(invoices, original, null);

        Assert.Equal(11_500, Assert.Single(invoices.Rows, i => i.Id != original.Id).CarriedCreditCents);
    }

    [Fact]
    public async Task Replacement_IsAFaithfulCopy()
    {
        var original = IssuedInvoice();
        var invoices = new InMemoryInvoiceRepository(original);

        await ReissueAsync(invoices, original, null);

        var replacement = Assert.Single(invoices.Rows, i => i.Id != original.Id);
        Assert.Equal(TenantId, replacement.TenantId);
        Assert.Equal(ActorUserId, replacement.CreatedBy);
        Assert.Equal(Later, replacement.CreatedAtUtc);
        Assert.Equal(original.Customer, replacement.Customer);
        Assert.Equal(original.Currency, replacement.Currency);
        Assert.Equal(original.Notes, replacement.Notes);
        Assert.Equal(original.Issuer, replacement.Issuer);
        Assert.Equal(original.GetTermsSnapshot(), replacement.GetTermsSnapshot());
        Assert.Equal(original.Total, replacement.Total);
        Assert.Null(replacement.InvoiceNumber);
        var sourceLines = original.Lines.ToList();
        var copiedLines = replacement.Lines.ToList();
        Assert.Equal(sourceLines.Count, copiedLines.Count);
        for (var i = 0; i < sourceLines.Count; i++)
        {
            Assert.Equal(sourceLines[i].Description, copiedLines[i].Description);
            Assert.Equal(sourceLines[i].Quantity, copiedLines[i].Quantity);
            Assert.Equal(sourceLines[i].UnitAmount, copiedLines[i].UnitAmount);
            Assert.Equal(sourceLines[i].TaxBasisPoints, copiedLines[i].TaxBasisPoints);
            Assert.Equal(sourceLines[i].CatalogItemId, copiedLines[i].CatalogItemId);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task NoReason_UsesTheDefaultReason(string? reason)
    {
        var original = IssuedInvoice();

        await ReissueAsync(new InMemoryInvoiceRepository(original), original, reason);

        Assert.Equal("Replaced by a corrected invoice.", original.VoidReason);
        var voided = Assert.Single(_bus.Published.OfType<InvoiceVoidedIntegrationEvent>());
        Assert.Equal("Replaced by a corrected invoice.", voided.Reason);
    }

    [Fact]
    public async Task Saved_PublishesInTheSameOrder()
    {
        var original = IssuedInvoice();

        var result = await ReissueAsync(new InMemoryInvoiceRepository(original), original, "typo");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantId.ToString(), _bus.TenantId);
        Assert.Collection(
            _bus.Published,
            message =>
            {
                var voided = Assert.IsType<InvoiceVoidedIntegrationEvent>(message);
                Assert.Equal(TenantId, voided.TenantId);
                Assert.Equal(original.Id, voided.InvoiceId);
                Assert.Equal("INV-2026-00001", voided.InvoiceNumber);
                Assert.Equal("typo", voided.Reason);
            },
            message => Assert.Equal(original.Id, Assert.IsType<GenerateInvoicePdfCommand>(message).InvoiceId),
            message => Assert.Equal(original.Id, Assert.IsType<SyncInvoiceStockCommand>(message).InvoiceId)
        );
    }

    [Fact]
    public async Task SaveFails_PublishesNothing()
    {
        var original = IssuedInvoice();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            ReissueAsync(new InMemoryInvoiceRepository(original), original, null, new FailingUnitOfWork())
        );

        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task UnknownInvoice_IsNotFound()
    {
        var result = await ReissueInvoiceHandler.Handle(
            new ReissueInvoiceCommand(TenantId, Guid.NewGuid(), ActorUserId, null, CanViewAll: true),
            new InMemoryInvoiceRepository(),
            Options.Create(new BillingVisibilityOptions()),
            _unitOfWork,
            _bus,
            new NoopCorrelationContext(),
            new FixedClock(Later),
            CancellationToken.None
        );

        Assert.Equal("Billing.Invoice.NotFound", result.Error.Code);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Draft_IsNotReissuable()
    {
        var draft = DraftInvoice();

        await AssertRejectedAsync(draft, "Billing.Invoice.NotReissuable");

        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Null(draft.ReplacedByInvoiceId);
    }

    [Fact]
    public async Task DeletedDraft_IsRejectedAsDeleted()
    {
        var draft = DraftInvoice();
        Assert.True(draft.SoftDeleteDraft(Now, ActorUserId).IsSuccess);

        await AssertRejectedAsync(draft, "Billing.Invoice.Deleted");
    }

    [Fact]
    public async Task AlreadyReplaced_IsRejected()
    {
        var original = IssuedInvoice();
        Pay(original, 3_000);
        var invoices = new InMemoryInvoiceRepository(original);
        Assert.True((await ReissueAsync(invoices, original, "first")).IsSuccess);
        var firstReplacement = original.ReplacedByInvoiceId;
        var saves = _unitOfWork.Saves;
        _bus.Published.Clear();

        var second = await ReissueAsync(invoices, original, "second");

        Assert.Equal("Billing.Invoice.AlreadyReplaced", second.Error.Code);
        Assert.Equal(firstReplacement, original.ReplacedByInvoiceId);
        Assert.Equal("first", original.VoidReason);
        Assert.Equal(2, invoices.Rows.Count);
        Assert.Equal(saves, _unitOfWork.Saves);
        Assert.Empty(_bus.Published);
    }

    // Hoy una anulada sin reemplazo se puede reemitir: conserva su motivo y el crédito igual se arrastra.
    [Fact]
    public async Task VoidedWithoutReplacement_IsReissued_KeepingItsVoidData()
    {
        var original = IssuedInvoice();
        Pay(original, 3_000);
        Assert.True(original.Void("first void", Now, ActorUserId).IsSuccess);
        var invoices = new InMemoryInvoiceRepository(original);

        var result = await ReissueAsync(invoices, original, "corrected");

        Assert.True(result.IsSuccess);
        Assert.Equal("first void", original.VoidReason);
        Assert.Equal(Now, original.VoidedAtUtc);
        Assert.Equal(result.Value.ReplacementInvoiceId, original.ReplacedByInvoiceId);
        Assert.Equal(3_000, Assert.Single(invoices.Rows, i => i.Id != original.Id).CarriedCreditCents);
        Assert.Equal("corrected", Assert.Single(_bus.Published.OfType<InvoiceVoidedIntegrationEvent>()).Reason);
    }

    [Fact]
    public async Task IssuedReplacement_ReceivesTheCredit_AsACarriedCreditPayment()
    {
        var original = IssuedInvoice();
        Pay(original, 6_000);
        var invoices = new InMemoryInvoiceRepository(original);
        await ReissueAsync(invoices, original, null);
        var replacement = Assert.Single(invoices.Rows, i => i.Id != original.Id);

        Assert.True(
            replacement
                .Issue(InvoiceNumber.Create("INV-2026-00002").Value, Later, Later.AddDays(30), ActorUserId)
                .IsSuccess
        );
        Assert.True(replacement.ApplyCarriedCredit(Later).IsSuccess);

        var payment = Assert.Single(replacement.Payments);
        Assert.Equal(InvoicePaymentSource.CarriedCredit, payment.Source);
        Assert.Equal(original.Id.ToString(), payment.SourceReference);
        Assert.Equal(6_000, payment.Amount.AmountCents);
        Assert.Equal(InvoiceStatus.PartiallyPaid, replacement.Status);
        Assert.Equal(4_000, replacement.AmountDue.AmountCents);
        Assert.Equal(0, replacement.CarriedCreditCents);
    }

    private async Task AssertRejectedAsync(Invoice invoice, string code)
    {
        var invoices = new InMemoryInvoiceRepository(invoice);

        var result = await ReissueAsync(invoices, invoice, null);

        Assert.Equal(code, result.Error.Code);
        Assert.Single(invoices.Rows);
        Assert.Equal(0, _unitOfWork.Saves);
        Assert.Empty(_bus.Published);
    }

    private Task<Result<ReissueInvoiceResult>> ReissueAsync(
        InMemoryInvoiceRepository invoices,
        Invoice invoice,
        string? reason,
        IUnitOfWork? unitOfWork = null
    ) =>
        ReissueInvoiceHandler.Handle(
            new ReissueInvoiceCommand(TenantId, invoice.Id, ActorUserId, reason, CanViewAll: true),
            invoices,
            Options.Create(new BillingVisibilityOptions()),
            unitOfWork ?? _unitOfWork,
            _bus,
            new NoopCorrelationContext(),
            new FixedClock(Later),
            CancellationToken.None
        );

    private static void Pay(Invoice invoice, long cents) =>
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

    private static Invoice IssuedInvoice()
    {
        var invoice = DraftInvoice();
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        return invoice;
    }

    private static Invoice DraftInvoice() =>
        Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [
                    new DraftInvoiceLine("Ink cartridge", 2, 4_000, 1_000, CatalogItemId),
                    new DraftInvoiceLine("Fee", 1, 1_200, 0),
                ],
                "Thanks",
                Now,
                new IssuerSnapshot(
                    "Office",
                    new Address("1 Main", null, "Miami", "FL", "33101", "US"),
                    null,
                    null,
                    null,
                    null
                ),
                new InvoiceTermsSnapshot(PaymentTerms.Net15, "Wire", "Footer")
            )
            .Value;

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated SQL timeout.");
    }
}
