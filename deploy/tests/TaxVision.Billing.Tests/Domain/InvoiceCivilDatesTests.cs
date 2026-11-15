using BuildingBlocks.TimeZones;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

/// <summary>
/// P6: la factura congela al emitir la fecha civil de la oficina. Emitida a las 20:00 de Nueva York el 31-dic
/// (01:00Z del 1-ene) es del 31-dic; con el día UTC salía del 1-ene.
/// </summary>
public sealed class InvoiceCivilDatesTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly DateTime NewYearsEveEvening = new(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Issuing_freezes_the_office_civil_dates_and_zone()
    {
        var invoice = Draft(new InvoiceTermsSnapshot(PaymentTerms.Net30, null, null));

        var issued = invoice.Issue(Number(), NewYearsEveEvening, NewYork, Guid.NewGuid());

        Assert.True(issued.IsSuccess);
        Assert.Equal(new DateOnly(2026, 12, 31), invoice.IssueDate);
        Assert.Equal(new DateOnly(2027, 1, 30), invoice.DueDate);
        Assert.Equal("America/New_York", invoice.IssueTimeZoneId);
        // Vence al final del día civil en la oficina (P4): 23:59:59 del 30-ene en Nueva York.
        Assert.Equal(new DateTime(2027, 1, 31, 4, 59, 59, DateTimeKind.Utc), invoice.DueDateUtc);
        Assert.Equal(NewYearsEveEvening, invoice.IssueDateUtc);
    }

    [Fact]
    public void Due_on_receipt_is_due_the_same_office_day()
    {
        var invoice = Draft(new InvoiceTermsSnapshot(PaymentTerms.DueOnReceipt, null, null));

        invoice.Issue(Number(), NewYearsEveEvening, NewYork, Guid.NewGuid());

        Assert.Equal(new DateOnly(2026, 12, 31), invoice.DueDate);
    }

    /// <summary>Una factura emitida antes no tiene fechas congeladas: el render usa la derivación de siempre.</summary>
    [Fact]
    public void Issuing_without_a_zone_leaves_the_civil_dates_empty()
    {
        var invoice = Draft(null);

        invoice.Issue(Number(), NewYearsEveEvening, NewYearsEveEvening.AddDays(30), Guid.NewGuid());

        Assert.Null(invoice.IssueDate);
        Assert.Null(invoice.DueDate);
        Assert.Null(invoice.IssueTimeZoneId);
    }

    /// <summary>El pago a las 21:00 del 14-ene en Nueva York (02:00Z del 15) es del 14 en la oficina.</summary>
    [Fact]
    public void Paying_freezes_the_office_paid_date()
    {
        var invoice = Draft(null);
        invoice.Issue(Number(), NewYearsEveEvening, NewYork, Guid.NewGuid());

        invoice.ApplyPayment(
            InvoicePaymentSource.Manual,
            "ref-1",
            Money.Create(10_000, "USD").Value,
            PaymentMethod.Cash,
            new DateTime(2027, 1, 15, 2, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid()
        );

        Assert.Equal(new DateOnly(2027, 1, 14), invoice.PaidDate);
    }

    /// <summary>TZ05-H7: el hash del recibo no depende del Kind (una fecha leída de la base viene sin Z).</summary>
    [Fact]
    public void The_receipt_hash_does_not_depend_on_the_date_kind()
    {
        var invoice = Paid(new DateTime(2027, 1, 15, 2, 0, 0, DateTimeKind.Unspecified));

        // La forma canónica lleva el instante en UTC con "Z", venga como venga el Kind.
        var canonical =
            $"{invoice.TenantId:N}|{invoice.Id:N}|{invoice.InvoiceNumber}|10000|USD|{PaymentMethod.Cash}|2027-01-15T02:00:00.0000000Z";
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))
        );
        Assert.Equal(expected, invoice.ReceiptHash);
    }

    private static Invoice Paid(DateTime paidAt)
    {
        var invoice = Draft(null);
        invoice.Issue(Number(), NewYearsEveEvening, NewYork, Guid.NewGuid());
        invoice.ApplyPayment(
            InvoicePaymentSource.Manual,
            "ref-h7",
            Money.Create(10_000, "USD").Value,
            PaymentMethod.Cash,
            paidAt,
            Guid.Empty
        );
        return invoice;
    }

    private static InvoiceNumber Number() => InvoiceNumber.Create("INV-2026-00001").Value;

    private static Invoice Draft(InvoiceTermsSnapshot? terms) =>
        Invoice
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", null, null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 10_000, 0)],
                null,
                NewYearsEveEvening,
                null,
                terms
            )
            .Value;
}
