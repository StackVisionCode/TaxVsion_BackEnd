using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

/// <summary>La regla "una reemisión no vuelve a cobrar lo ya pagado" vive en el agregado (BIL-13).</summary>
public sealed class InvoiceReissueTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly Guid CatalogItemId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ReissueAs_CarriesSumOfPayments_AndVoidsOriginal()
    {
        var original = IssuedInvoice();
        Pay(original, 6_000);
        Pay(original, 3_500);
        var replacement = original.CreateReplacementDraft(ActorUserId, Now).Value;

        var result = original.ReissueAs(replacement, "  wrong client ", Now, ActorUserId);

        Assert.Equal("wrong client", result.Value);
        Assert.Equal(original.Payments.Sum(p => p.Amount.AmountCents), replacement.CarriedCreditCents);
        Assert.Equal(9_500, replacement.CarriedCreditCents);
        Assert.Equal(InvoiceStatus.Voided, original.Status);
        Assert.Equal("wrong client", original.VoidReason);
        Assert.Equal(replacement.Id, original.ReplacedByInvoiceId);
        Assert.Equal(original.Id, replacement.ReplacesInvoiceId);
    }

    [Fact]
    public void ReissueAs_WithoutReason_UsesTheDefaultReason()
    {
        var original = IssuedInvoice();
        var replacement = original.CreateReplacementDraft(ActorUserId, Now).Value;

        var result = original.ReissueAs(replacement, null, Now, ActorUserId);

        Assert.Equal(Invoice.DefaultReissueReason, result.Value);
        Assert.Equal(Invoice.DefaultReissueReason, original.VoidReason);
    }

    [Fact]
    public void ReissueAs_Twice_IsRejected()
    {
        var original = IssuedInvoice();
        var first = original.CreateReplacementDraft(ActorUserId, Now).Value;
        Assert.True(original.ReissueAs(first, null, Now, ActorUserId).IsSuccess);
        var second = original.CreateReplacementDraft(ActorUserId, Now).Value;

        var result = original.ReissueAs(second, null, Now, ActorUserId);

        Assert.Equal("Billing.Invoice.AlreadyReplaced", result.Error.Code);
        Assert.Equal(first.Id, original.ReplacedByInvoiceId);
    }

    [Fact]
    public void ReissueAs_OnADraft_IsRejected()
    {
        var draft = DraftInvoice();
        var replacement = draft.CreateReplacementDraft(ActorUserId, Now).Value;

        var result = draft.ReissueAs(replacement, null, Now, ActorUserId);

        Assert.Equal("Billing.Invoice.NotReissuable", result.Error.Code);
        Assert.Equal(InvoiceStatus.Draft, draft.Status);
    }

    [Fact]
    public void ReissueAs_WithAReplacementAlreadyIssued_IsRejected()
    {
        var original = IssuedInvoice();
        var replacement = IssuedInvoice();

        var result = original.ReissueAs(replacement, null, Now, ActorUserId);

        Assert.Equal("Billing.Invoice.NotDraft", result.Error.Code);
        Assert.Equal(InvoiceStatus.Issued, original.Status);
        Assert.Null(original.ReplacedByInvoiceId);
    }

    [Fact]
    public void CreateReplacementDraft_KeepsCatalogItems_SoStockIsRedoneOnIssue()
    {
        var original = IssuedInvoice();

        var replacement = original.CreateReplacementDraft(ActorUserId, Now).Value;

        Assert.Equal(CatalogItemId, Assert.Single(replacement.Lines).CatalogItemId);
        Assert.Equal(original.Total, replacement.Total);
        Assert.Equal(InvoiceStatus.Draft, replacement.Status);
    }

    [Fact]
    public void HasBeenIssued_IsFalseOnlyForADraft()
    {
        Assert.False(DraftInvoice().HasBeenIssued);
        Assert.True(IssuedInvoice().HasBeenIssued);
    }

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
                [new DraftInvoiceLine("Ink cartridge", 2, 5_000, 0, CatalogItemId)],
                null,
                Now
            )
            .Value;
}
