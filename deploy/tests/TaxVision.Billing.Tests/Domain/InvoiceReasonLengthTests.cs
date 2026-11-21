using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

/// <summary>
/// El motivo que escribe la oficina (anular, reemitir, cambiar estado) tiene tope: las columnas son de 500/512 y uno
/// más largo terminaba en un error 500 al guardar. La corrección de estado se cubre en <see cref="InvoiceStatusOverrideTests"/>.
/// </summary>
public sealed class InvoiceReasonLengthTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string TooLong = new('x', 501);
    private static readonly string Longest = new('x', 500);

    [Fact]
    public void Voiding_with_a_reason_that_is_too_long_is_rejected()
    {
        var invoice = IssuedInvoice();

        var result = invoice.Void(TooLong, Now, ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.ReasonTooLong", result.Error.Code);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Null(invoice.VoidReason);
    }

    [Fact]
    public void Voiding_with_the_longest_reason_allowed_works()
    {
        var invoice = IssuedInvoice();

        var result = invoice.Void($" {Longest} ", Now, ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(Longest, invoice.VoidReason);
    }

    [Fact]
    public void Reissuing_with_a_reason_that_is_too_long_is_rejected_and_links_nothing()
    {
        var original = IssuedInvoice();
        var replacement = original.CreateReplacementDraft(ActorUserId, Now).Value;

        var result = original.ReissueAs(replacement, TooLong, Now, ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.ReasonTooLong", result.Error.Code);
        Assert.Equal(InvoiceStatus.Issued, original.Status);
        Assert.Null(original.ReplacedByInvoiceId);
        Assert.Null(replacement.ReplacesInvoiceId);
    }

    [Fact]
    public void Changing_status_with_a_reason_that_is_too_long_is_rejected()
    {
        var invoice = IssuedInvoice();

        var result = invoice.ChangeStatus(InvoiceStatus.Sent, ActorUserId, TooLong, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.ReasonTooLong", result.Error.Code);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
    }

    [Fact]
    public void Changing_status_without_a_reason_still_works()
    {
        var invoice = IssuedInvoice();

        Assert.True(invoice.ChangeStatus(InvoiceStatus.Sent, ActorUserId, null, Now).IsSuccess);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

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
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        return invoice;
    }
}
