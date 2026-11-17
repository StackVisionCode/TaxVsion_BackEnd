using TaxVision.Documents.Application.Generations.OnboardingReceipt;
using TaxVision.Documents.Application.Generations.Receipts;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>El recibo del alta lista cada código que Auth aplicó; sin la lista, el bruto deja una sola fila.</summary>
public sealed class OnboardingReceiptPayloadTests
{
    [Fact]
    public void Each_code_applied_at_signup_is_a_row()
    {
        var payload = Payload() with
        {
            GrossAmountCents = 6_400,
            Discounts =
            [
                new ReceiptDiscount("Referral discount", 500),
                new ReceiptDiscount("Promo code SPRI...0420", 1_000),
            ],
        };

        Assert.Equal(
            [new ReceiptDiscount("Referral discount", 500), new ReceiptDiscount("Promo code SPRI...0420", 1_000)],
            payload.DiscountRows()
        );
    }

    [Fact]
    public void A_gross_without_rows_shows_one_discount_row()
    {
        var payload = Payload() with { GrossAmountCents = 6_400 };

        Assert.Equal([new ReceiptDiscount("Discount", 1_500)], payload.DiscountRows());
    }

    [Fact]
    public void An_old_payload_shows_no_discount()
    {
        Assert.Empty(Payload().DiscountRows());
    }

    private static OnboardingReceiptPayload Payload() =>
        new(
            "Ada",
            "Lovelace",
            "ada@example.com",
            "Professional",
            4_900,
            "USD",
            new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc),
            "4242",
            null
        );
}
