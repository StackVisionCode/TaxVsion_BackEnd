using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>Catálogo en memoria con una versión vigente (SMS a 10_000 micros por defecto).</summary>
public sealed class FakePriceBookRepository : IPriceBookRepository
{
    public PriceBookVersion? Active { get; set; } =
        PriceBookVersion
            .Publish(1, new Dictionary<PriceChannel, long> { [PriceChannel.Sms] = 10_000 }, Guid.NewGuid())
            .Value;

    public Task<PriceBookVersion?> GetActiveVersionAsync(CancellationToken ct = default) => Task.FromResult(Active);

    public Task<int> GetMaxVersionAsync(CancellationToken ct = default) => Task.FromResult(Active?.Version ?? 0);

    public Task AddVersionAsync(PriceBookVersion version, CancellationToken ct = default)
    {
        Active = version;
        return Task.CompletedTask;
    }
}
