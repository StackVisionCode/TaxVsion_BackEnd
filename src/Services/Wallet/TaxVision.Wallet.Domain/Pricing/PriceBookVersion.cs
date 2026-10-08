using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Pricing;

/// <summary>
/// Versión del catálogo de precios (00_Plan §4, ADR-WAL-007) — **global de plataforma** (NO tenant-owned),
/// **inmutable** una vez publicada. Cada cambio de tarifa = una versión nueva; una cotización/reserva fija
/// su <see cref="Version"/>, así que cambiar precios no altera ejecuciones ya autorizadas. La versión
/// vigente = la de mayor <see cref="Version"/> publicada con <see cref="EffectiveFromUtc"/> ≤ ahora.
/// </summary>
public sealed class PriceBookVersion : BaseEntity
{
    private readonly List<PriceRule> _rules = [];

    private PriceBookVersion() { }

    public int Version { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<PriceRule> Rules => _rules.AsReadOnly();

    /// <summary>Publica una versión nueva e inmutable con las tarifas dadas (una por canal).</summary>
    public static Result<PriceBookVersion> Publish(
        int version,
        IReadOnlyDictionary<PriceChannel, long> unitPriceMicrosByChannel,
        Guid createdByUserId,
        DateTime? effectiveFromUtc = null
    )
    {
        if (version <= 0)
            return Result.Failure<PriceBookVersion>(new Error("Pricing.VersionInvalid", "Version must be positive."));
        if (unitPriceMicrosByChannel.Count == 0)
            return Result.Failure<PriceBookVersion>(
                new Error("Pricing.NoRules", "At least one channel price is required.")
            );
        if (unitPriceMicrosByChannel.Values.Any(p => p < 0))
            return Result.Failure<PriceBookVersion>(
                new Error("Pricing.PriceNegative", "Unit price cannot be negative.")
            );

        var pbv = new PriceBookVersion
        {
            Id = Guid.NewGuid(),
            Version = version,
            EffectiveFromUtc = effectiveFromUtc ?? DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        foreach (var (channel, price) in unitPriceMicrosByChannel)
            pbv._rules.Add(PriceRule.Create(pbv.Id, channel, price));
        return Result.Success(pbv);
    }

    /// <summary>Precio por unidad del canal, o null si el catálogo no lo define (NUNCA se asume 0).</summary>
    public long? UnitPriceMicros(PriceChannel channel) =>
        _rules.FirstOrDefault(r => r.Channel == channel)?.UnitPriceMicros;
}

/// <summary>Tarifa de un canal dentro de una <see cref="PriceBookVersion"/> (inmutable).</summary>
public sealed class PriceRule : BaseEntity
{
    private PriceRule() { }

    public Guid PriceBookVersionId { get; private set; }
    public PriceChannel Channel { get; private set; }
    public long UnitPriceMicros { get; private set; }

    internal static PriceRule Create(Guid versionId, PriceChannel channel, long unitPriceMicros) =>
        new()
        {
            Id = Guid.NewGuid(),
            PriceBookVersionId = versionId,
            Channel = channel,
            UnitPriceMicros = unitPriceMicros,
        };
}
