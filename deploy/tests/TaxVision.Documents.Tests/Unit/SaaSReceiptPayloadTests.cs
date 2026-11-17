using System.Text.Json;
using TaxVision.Documents.Application.Generations.Receipts;
using TaxVision.Documents.Application.Generations.SaaSReceipt;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>El payload viaja serializado en un comando durable: un mensaje de antes del "Billed to" debe
/// seguir deserializando.</summary>
public sealed class SaaSReceiptPayloadTests
{
    [Fact]
    public void A_payload_serialized_before_billed_to_still_deserializes_with_nulls()
    {
        const string legacy = """
            {"OfficeName":"CoreTaxPro","Description":"Subscription renewal","AmountPaidCents":4900,"Currency":"USD",
             "PaidAtUtc":"2026-11-03T12:00:00Z","TransactionReferenceMask":"4242","Quantity":null,
             "UnitAmountCents":null,"SecondaryDescription":null}
            """;

        var payload = JsonSerializer.Deserialize<SaaSReceiptPayload>(legacy);

        Assert.NotNull(payload);
        Assert.Equal("CoreTaxPro", payload.OfficeName);
        Assert.Equal(4900, payload.AmountPaidCents);
        Assert.Null(payload.BilledToLegalName);
        Assert.Null(payload.BilledToEin);
        Assert.Null(payload.BilledToAddress);
    }

    [Fact]
    public void Billed_to_survives_a_round_trip()
    {
        var payload = new SaaSReceiptPayload(
            "CoreTaxPro",
            "Subscription renewal",
            4900,
            "USD",
            new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc),
            "4242",
            BilledToLegalName: "Core Tax Professionals LLC",
            BilledToEin: "12-3456789",
            BilledToAddress: "1600 Main St, Miami, FL 33101"
        );

        var copy = JsonSerializer.Deserialize<SaaSReceiptPayload>(JsonSerializer.Serialize(payload));

        Assert.Equal(payload, copy);
    }

    [Fact]
    public void The_discount_of_the_charge_is_one_row_with_its_label()
    {
        var payload = Payload(1_200) with
        {
            DiscountAmountCents = 300,
            DiscountLabel = "Promo code SPRI...0420 · 20% off",
        };

        var row = Assert.Single(payload.DiscountRows());

        Assert.Equal(new ReceiptDiscount("Promo code SPRI...0420 · 20% off", 300), row);
    }

    [Fact]
    public void A_discount_without_label_reads_discount()
    {
        var payload = Payload(1_200) with { DiscountAmountCents = 300 };

        Assert.Equal("Discount", Assert.Single(payload.DiscountRows()).Label);
    }

    // Cobros de antes: sus líneas ya suman lo pagado, así que el descuento no se vuelve a restar.
    [Fact]
    public void Old_lines_that_add_up_to_the_paid_amount_show_no_discount()
    {
        var payload = Payload(1_200) with
        {
            DiscountAmountCents = 300,
            Lines = [new BuildingBlocks.Messaging.PaymentAppIntegrationEvents.SaaSChargeLine("Seats", 2, 600)],
        };

        Assert.Empty(payload.DiscountRows());
    }

    private static SaaSReceiptPayload Payload(long paidCents) =>
        new(
            "CoreTaxPro",
            "Extra seats",
            paidCents,
            "USD",
            new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc),
            "4242"
        );
}
