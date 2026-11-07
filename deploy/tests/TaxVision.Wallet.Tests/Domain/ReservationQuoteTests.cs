using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Tests.Domain;

public sealed class ReservationQuoteTests
{
    private static PriceBookVersion Version() =>
        PriceBookVersion
            .Publish(
                1,
                new Dictionary<PriceChannel, long> { [PriceChannel.Email] = 1_234, [PriceChannel.Sms] = 10_000 },
                Guid.NewGuid()
            )
            .Value;

    [Fact]
    public void Calculate_UnpricedChannel_Fails()
    {
        var result = ReservationQuote.Calculate(Version(), [(PriceChannel.WhatsApp, 1L)]);

        Assert.True(result.IsFailure);
        Assert.Equal(PricingErrors.ChannelNotPriced("WhatsApp"), result.Error);
    }

    [Fact]
    public void Calculate_SumsPricePerUnitOfEachChannel()
    {
        var result = ReservationQuote.Calculate(Version(), [(PriceChannel.Email, 3L), (PriceChannel.Sms, 7L)]);

        Assert.Equal(73_702, result.Value.CostMicros);
        Assert.Equal(10, result.Value.Units);
    }

    [Fact]
    public void Calculate_NoUnits_CostsNothing()
    {
        var result = ReservationQuote.Calculate(Version(), []);

        Assert.Equal(0, result.Value.CostMicros);
        Assert.Equal(0, result.Value.Units);
    }
}
