using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Pricing;

public static class PricingErrors
{
    public static readonly Error NoActiveVersion = new(
        "Pricing.NoActiveVersion",
        "No hay un catálogo de precios vigente."
    );

    public static Error ChannelNotPriced(string channel) =>
        new("Pricing.ChannelNotPriced", $"El canal '{channel}' no tiene tarifa en el catálogo vigente.");
}
