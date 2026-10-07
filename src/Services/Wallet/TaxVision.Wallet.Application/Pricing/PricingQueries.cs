using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;

namespace TaxVision.Wallet.Application.Pricing;

public static class PricingErrors
{
    public static readonly Error NoActiveVersion = new(
        "Pricing.NoActiveVersion",
        "No hay un catálogo de precios vigente."
    );

    public static Error ChannelNotPriced(string channel) =>
        new("Pricing.ChannelNotPriced", $"El canal '{channel}' no tiene tarifa en el catálogo vigente.");
}

// ─────────── GET /wallet/rates ───────────
public sealed record GetRatesQuery;

public static class GetRatesHandler
{
    public static async Task<Result<RatesView>> Handle(
        GetRatesQuery query,
        IPriceBookRepository priceBook,
        CancellationToken ct
    )
    {
        var active = await priceBook.GetActiveVersionAsync(ct);
        if (active is null)
            return Result.Failure<RatesView>(PricingErrors.NoActiveVersion);

        var rates = active
            .Rules.Select(r => new RateView(r.Channel.ToString(), r.UnitPriceMicros))
            .OrderBy(r => r.Channel)
            .ToList();
        return Result.Success(new RatesView(active.Version, active.EffectiveFromUtc, rates));
    }
}

// ─────────── POST /wallet/estimate ───────────
public sealed record EstimateQuery(Guid TenantId, PerChannelUnits Units);

public static class EstimateHandler
{
    public static async Task<Result<EstimateView>> Handle(
        EstimateQuery query,
        IPriceBookRepository priceBook,
        IWalletRepository wallets,
        CancellationToken ct
    )
    {
        var active = await priceBook.GetActiveVersionAsync(ct);
        if (active is null)
            return Result.Failure<EstimateView>(PricingErrors.NoActiveVersion);

        long cost = 0;
        var lines = new List<EstimateLine>();
        foreach (var (channel, units) in query.Units.NonZero())
        {
            var price = active.UnitPriceMicros(channel);
            if (price is null)
                return Result.Failure<EstimateView>(PricingErrors.ChannelNotPriced(channel.ToString()));
            var subtotal = units * price.Value;
            cost += subtotal;
            lines.Add(new EstimateLine(channel.ToString(), units, price.Value, subtotal));
        }

        var wallet = await wallets.GetByTenantAsync(query.TenantId, ct);
        var available = wallet?.AvailableMicros ?? 0;
        var currency = wallet?.Currency ?? Domain.Wallet.Wallet.DefaultCurrency;
        var sufficient = available >= cost;

        return Result.Success(
            new EstimateView(
                active.Version,
                cost,
                available,
                sufficient,
                sufficient ? 0 : cost - available,
                currency,
                lines
            )
        );
    }
}
