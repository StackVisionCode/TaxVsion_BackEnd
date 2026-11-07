using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

public sealed class InvoiceNumberTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("INV-26-00001")]
    [InlineData("INV-2026-0001")]
    [InlineData("REC-2026-00001")]
    [InlineData("INV-2026-00001-x")]
    public void Create_InvalidFormat_Fails(string value)
    {
        var result = InvoiceNumber.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.InvoiceNumber.Invalid", result.Error.Code);
    }

    [Theory]
    [InlineData("INV-2026-00001")]
    [InlineData("INV-2026-123456")]
    [InlineData("ONB-2026-00001")]
    public void Create_ServerAssignedFormats_Succeed(string value)
    {
        Assert.Equal(value, InvoiceNumber.Create(value).Value.Value);
    }

    [Fact]
    public void Issue_WithVo_StoresNormalizedNumber()
    {
        var invoice = Invoice
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Ink cartridge", 2, 5_000, 0, null)],
                null,
                Now
            )
            .Value;

        var number = InvoiceNumber.Create("  inv-2026-00042 ").Value;
        var issued = invoice.Issue(number, Now, Now.AddDays(30), Guid.NewGuid());

        Assert.True(issued.IsSuccess);
        Assert.Equal("INV-2026-00042", invoice.InvoiceNumber);
    }
}
