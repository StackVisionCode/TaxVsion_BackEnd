using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Pricing;

/// <summary>Cotización de una reserva: precio por canal × unidades, con la versión vigente del catálogo.</summary>
public static class ReservationQuote
{
    public static Result<(long CostMicros, int Units)> Calculate(
        PriceBookVersion activeVersion,
        IEnumerable<(PriceChannel Channel, long Units)> units
    )
    {
        long cost = 0;
        var totalUnits = 0;
        foreach (var (channel, channelUnits) in units)
        {
            var price = activeVersion.UnitPriceMicros(channel);
            if (price is null)
                return Result.Failure<(long, int)>(PricingErrors.ChannelNotPriced(channel.ToString()));
            cost += channelUnits * price.Value;
            totalUnits += (int)channelUnits;
        }

        return Result.Success((cost, totalUnits));
    }
}
