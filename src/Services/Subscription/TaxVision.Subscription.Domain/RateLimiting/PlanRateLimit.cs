using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.RateLimiting;

/// <summary>
/// Multiplicador (o piso/techo negociado) de cuota de rate-limit para una
/// <see cref="RateLimitCategory"/> dentro de un plan comercial — Plan_Implementacion_Fases.md
/// §5. Solo cubre categorías con tenant (Bloque II-IV, F..O): A-E son pre-auth/webhook y nunca
/// escalan por plan (invariante §3.6), no tienen fila acá. Catálogo global, no ITenantOwned —
/// sembrado por migración, ver <c>PlanRateLimitSeeder</c>.
/// </summary>
public sealed class PlanRateLimit : BaseEntity
{
    public PlanCode PlanCode { get; private set; } = null!;
    public RateLimitCategory Category { get; private set; }
    public decimal MultiplierOverride { get; private set; }

    /// <summary>
    /// Cupo fijo negociado que reemplaza por completo el cálculo por multiplicador —
    /// reservado para planes Enterprise Custom (§5, sin PlanCode propio todavía). Null en
    /// todas las filas sembradas en Fase 1.
    /// </summary>
    public int? HardOverridePerMinute { get; private set; }

    private PlanRateLimit() { }

    public static Result<PlanRateLimit> Seed(
        Guid id,
        PlanCode planCode,
        RateLimitCategory category,
        decimal multiplierOverride,
        int? hardOverridePerMinute = null
    )
    {
        if (multiplierOverride <= 0)
        {
            return Result.Failure<PlanRateLimit>(
                new Error("PlanRateLimit.InvalidMultiplier", "Multiplier override must be greater than zero.")
            );
        }

        if (hardOverridePerMinute is <= 0)
        {
            return Result.Failure<PlanRateLimit>(
                new Error("PlanRateLimit.InvalidHardOverride", "Hard override, when set, must be greater than zero.")
            );
        }

        return Result.Success(
            new PlanRateLimit
            {
                Id = id,
                PlanCode = planCode,
                Category = category,
                MultiplierOverride = multiplierOverride,
                HardOverridePerMinute = hardOverridePerMinute,
            }
        );
    }
}
