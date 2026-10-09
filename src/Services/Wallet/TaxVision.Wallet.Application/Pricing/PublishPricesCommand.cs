using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Application.Pricing;

/// <summary>
/// Publica una versión nueva del catálogo (PlatformAdmin). Inmutable: no edita la vigente, crea otra con
/// número mayor que pasa a ser la vigente. Las ejecuciones ya autorizadas conservan su versión.
/// </summary>
public sealed record PublishPricesCommand(Guid UserId, PerChannelUnitPrices Prices);

/// <summary>Precios por canal a publicar (micros). Null = no se fija ese canal.</summary>
public sealed record PerChannelUnitPrices(long? Email, long? Sms, long? Push, long? WhatsApp)
{
    public IReadOnlyDictionary<PriceChannel, long> ToMap()
    {
        var map = new Dictionary<PriceChannel, long>();
        if (Email is not null)
            map[PriceChannel.Email] = Email.Value;
        if (Sms is not null)
            map[PriceChannel.Sms] = Sms.Value;
        if (Push is not null)
            map[PriceChannel.Push] = Push.Value;
        if (WhatsApp is not null)
            map[PriceChannel.WhatsApp] = WhatsApp.Value;
        return map;
    }
}

public static class PublishPricesHandler
{
    public static async Task<Result<RatesView>> Handle(
        PublishPricesCommand command,
        IPriceBookRepository priceBook,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nextVersion = await priceBook.GetMaxVersionAsync(ct) + 1;
        var created = PriceBookVersion.Publish(nextVersion, command.Prices.ToMap(), command.UserId);
        if (created.IsFailure)
            return Result.Failure<RatesView>(created.Error);

        await priceBook.AddVersionAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var rates = created
            .Value.Rules.Select(r => new RateView(r.Channel.ToString(), r.UnitPriceMicros))
            .OrderBy(r => r.Channel)
            .ToList();
        return Result.Success(new RatesView(created.Value.Version, created.Value.EffectiveFromUtc, rates));
    }
}
