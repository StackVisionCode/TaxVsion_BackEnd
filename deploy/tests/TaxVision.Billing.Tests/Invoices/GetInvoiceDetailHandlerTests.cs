using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GetInvoiceDetail;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// El detalle es donde queda a la vista lo que la corrección de estado dejó pendiente: un crédito arrastrado que se
/// revirtió ya se cobró en la factura reemplazada y quedó sin aplicar.
/// </summary>
public sealed class GetInvoiceDetailHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_reversed_carried_credit_is_shown_as_credit_left_unapplied()
    {
        var replacement = ReplacementPaidWithCarriedCredit();
        Assert.True(
            replacement.OverrideStatus(InvoiceStatus.Issued, "Wrong amount", Now.AddHours(1), ActorUserId).IsSuccess
        );

        var detail = await DetailAsync(replacement);

        var payment = Assert.Single(detail.Payments!);
        Assert.Equal("CarriedCredit", payment.Source);
        Assert.NotNull(payment.ReversedAtUtc);
        Assert.True(payment.CreditLeftUnapplied);
        Assert.False(payment.NeedsProviderRefund);
    }

    [Fact]
    public async Task A_carried_credit_that_still_counts_is_not_flagged()
    {
        var detail = await DetailAsync(ReplacementPaidWithCarriedCredit());

        var payment = Assert.Single(detail.Payments!);
        Assert.Equal("CarriedCredit", payment.Source);
        Assert.False(payment.CreditLeftUnapplied);
    }

    private static async Task<InvoiceDetailResponse> DetailAsync(Invoice invoice)
    {
        var result = await GetInvoiceDetailHandler.Handle(
            new GetInvoiceDetailQuery(TenantId, invoice.Id, ActorUserId, CanViewAll: true),
            new SingleInvoice(invoice),
            Options.Create(new BillingVisibilityOptions()),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static Invoice ReplacementPaidWithCarriedCredit()
    {
        var original = IssuedInvoice("INV-2026-00001");
        Assert.True(
            original
                .ApplyPayment(
                    InvoicePaymentSource.Online,
                    "pay-1",
                    Money.Create(10_000, "USD").Value,
                    PaymentMethod.Online,
                    Now,
                    ActorUserId
                )
                .IsSuccess
        );
        var replacement = original.CreateReplacementDraft(ActorUserId, Now).Value;
        Assert.True(original.ReissueAs(replacement, null, Now, ActorUserId).IsSuccess);
        Assert.True(
            replacement.Issue(InvoiceNumber.Create("INV-2026-00002").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        Assert.True(replacement.ApplyCarriedCredit(Now).IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, replacement.Status);
        return replacement;
    }

    private static Invoice IssuedInvoice(string number)
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
        Assert.True(invoice.Issue(InvoiceNumber.Create(number).Value, Now, Now.AddDays(30), ActorUserId).IsSuccess);
        return invoice;
    }

    private sealed class SingleInvoice(Invoice invoice) : IInvoiceRepository
    {
        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => Task.FromResult<Invoice?>(invoice);

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
