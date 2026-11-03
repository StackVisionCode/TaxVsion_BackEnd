using System.Text.Json;
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
}
