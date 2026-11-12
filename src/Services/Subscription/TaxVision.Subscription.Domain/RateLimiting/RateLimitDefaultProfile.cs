using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.RateLimiting;

/// <summary>Multiplicador del cupo base de una categoría. Nunca por debajo del base (D12).</summary>
public sealed record RateLimitMultiplier
{
    public const decimal Base = 1.0m;
    public const decimal Max = 1000m;

    public decimal Value { get; }

    private RateLimitMultiplier(decimal value) => Value = value;

    public static Result<RateLimitMultiplier> Create(decimal value)
    {
        if (value < Base)
            return Result.Failure<RateLimitMultiplier>(
                new Error(
                    "RateLimit.MultiplierBelowBase",
                    "A rate limit multiplier cannot be lower than 1.0 (the base quota)."
                )
            );

        if (value > Max)
            return Result.Failure<RateLimitMultiplier>(
                new Error("RateLimit.MultiplierTooHigh", $"A rate limit multiplier cannot exceed {Max}.")
            );

        return Result.Success(new RateLimitMultiplier(value));
    }
}

/// <summary>Multiplicador del perfil para una categoría.</summary>
public sealed class RateLimitProfileEntry
{
    private RateLimitProfileEntry() { }

    internal RateLimitProfileEntry(RateLimitCategory category, decimal multiplier)
    {
        Category = category;
        Multiplier = multiplier;
    }

    public RateLimitCategory Category { get; private set; }
    public decimal Multiplier { get; internal set; }
}

/// <summary>
/// D12: el cupo de un plan que no tiene fila para una categoría. Uno solo, de plataforma, sembrado con los valores de
/// Enterprise. Un plan nuevo nace con sus filas copiadas de acá y el catálogo que lee la flota completa lo que falte
/// con este perfil, así nadie cae al cupo base por omisión. Solo cubre las categorías con tenant (F–O): A–E son
/// pre-auth y webhooks y nunca escalan por plan.
/// </summary>
public sealed class RateLimitDefaultProfile : BaseEntity
{
    public static readonly Guid PlatformId = new("c4000000-0000-0000-0000-000000000001");

    public static IReadOnlyList<RateLimitCategory> ScalableCategories { get; } =
    [
        RateLimitCategory.F,
        RateLimitCategory.G,
        RateLimitCategory.H,
        RateLimitCategory.I,
        RateLimitCategory.J,
        RateLimitCategory.K,
        RateLimitCategory.L,
        RateLimitCategory.M,
        RateLimitCategory.N,
        RateLimitCategory.O,
    ];

    /// <summary>Los multiplicadores de Enterprise el día que se creó el perfil (M y N no escalan).</summary>
    public static IReadOnlyDictionary<RateLimitCategory, decimal> EnterpriseDefaults { get; } =
        new Dictionary<RateLimitCategory, decimal>
        {
            [RateLimitCategory.F] = 10m,
            [RateLimitCategory.G] = 10m,
            [RateLimitCategory.H] = 10m,
            [RateLimitCategory.I] = 10m,
            [RateLimitCategory.J] = 10m,
            [RateLimitCategory.K] = 20m,
            [RateLimitCategory.L] = 10m,
            [RateLimitCategory.M] = 1m,
            [RateLimitCategory.N] = 1m,
            [RateLimitCategory.O] = 10m,
        };

    private readonly List<RateLimitProfileEntry> _entries = [];

    private RateLimitDefaultProfile() { }

    public IReadOnlyCollection<RateLimitProfileEntry> Entries => _entries;
    public byte[] RowVersion { get; private set; } = [];
    public DateTime UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedBy { get; private set; }

    public static RateLimitDefaultProfile Seed(DateTime nowUtc)
    {
        var profile = new RateLimitDefaultProfile { Id = PlatformId, UpdatedAtUtc = nowUtc };
        foreach (var category in ScalableCategories)
            profile._entries.Add(new RateLimitProfileEntry(category, EnterpriseDefaults[category]));
        return profile;
    }

    public RateLimitMultiplier MultiplierFor(RateLimitCategory category)
    {
        var entry = _entries.FirstOrDefault(item => item.Category == category);
        return RateLimitMultiplier.Create(entry?.Multiplier ?? RateLimitMultiplier.Base).Value;
    }

    public Result Revise(RateLimitCategory category, RateLimitMultiplier multiplier, Guid actorUserId, DateTime nowUtc)
    {
        if (!ScalableCategories.Contains(category))
            return Result.Failure(
                new Error("RateLimit.CategoryNotScalable", $"Category {category} never scales by plan.")
            );

        var entry = _entries.FirstOrDefault(item => item.Category == category);
        if (entry is null)
            _entries.Add(new RateLimitProfileEntry(category, multiplier.Value));
        else if (entry.Multiplier == multiplier.Value)
            return Result.Success();
        else
            entry.Multiplier = multiplier.Value;

        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
        return Result.Success();
    }
}
