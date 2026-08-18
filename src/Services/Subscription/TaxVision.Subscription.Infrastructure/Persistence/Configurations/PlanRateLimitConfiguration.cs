using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class PlanRateLimitConfiguration : IEntityTypeConfiguration<PlanRateLimit>
{
    public void Configure(EntityTypeBuilder<PlanRateLimit> builder)
    {
        builder.ToTable("PlanRateLimits");
        builder.HasKey(rateLimit => rateLimit.Id);

        // Id determinista sembrado por migración (mismo patrón que PlanEntitlementDefinition).
        builder.Property(rateLimit => rateLimit.Id).ValueGeneratedNever();

        builder
            .Property(rateLimit => rateLimit.PlanCode)
            .HasConversion(code => code.Value, value => PlanCode.Create(value).Value)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(rateLimit => rateLimit.Category).HasConversion<string>().HasMaxLength(1).IsRequired();

        builder.Property(rateLimit => rateLimit.MultiplierOverride).HasColumnType("decimal(9,4)").IsRequired();

        builder.Property(rateLimit => rateLimit.HardOverridePerMinute);

        builder.HasIndex(rateLimit => new { rateLimit.PlanCode, rateLimit.Category }).IsUnique();

        // Se pasan los valores CLR (VO/enum) sin pre-convertir — HasData aplica los
        // HasConversion configurados arriba al generar el InsertData de la migración.
        builder.HasData(
            SeedRows.Select(row => new
            {
                row.Id,
                row.PlanCode,
                row.Category,
                row.MultiplierOverride,
                row.HardOverridePerMinute,
            })
        );
    }

    /// <summary>
    /// Multiplicadores iniciales — Plan_Implementacion_Fases.md §5, adaptado a los 3 PlanCode
    /// reales del catálogo (<see cref="PlanCatalog"/>: starter/pro/enterprise, no los nombres
    /// "Free/Standard/Plus/Enterprise/Custom" del doc de diseño — ver ADR_017 §"Corrección de
    /// nomenclatura"). Solo cubre F..O (Bloque II-IV) — A..E nunca escalan por plan (§3.6).
    /// M y N nunca escalan (§5 excepción) — multiplicador 1.0 explícito en todos los planes en
    /// vez de omitir la fila, para que la partición sea siempre explícita (invariante §3.2).
    /// </summary>
    private static readonly IReadOnlyList<PlanRateLimit> SeedRows = BuildSeedRows();

    private static IReadOnlyList<PlanRateLimit> BuildSeedRows()
    {
        var starter = PlanCode.Create(PlanCatalog.Starter).Value;
        var pro = PlanCode.Create(PlanCatalog.Pro).Value;
        var enterprise = PlanCode.Create(PlanCatalog.Enterprise).Value;

        (PlanCode Plan, RateLimitCategory Category, decimal Multiplier)[] rows =
        [
            // starter — baseline ("Standard" en el doc de diseño), ×1.0 en todo.
            (starter, RateLimitCategory.F, 1.0m),
            (starter, RateLimitCategory.G, 1.0m),
            (starter, RateLimitCategory.H, 1.0m),
            (starter, RateLimitCategory.I, 1.0m),
            (starter, RateLimitCategory.J, 1.0m),
            (starter, RateLimitCategory.K, 1.0m),
            (starter, RateLimitCategory.L, 1.0m),
            (starter, RateLimitCategory.M, 1.0m),
            (starter, RateLimitCategory.N, 1.0m),
            (starter, RateLimitCategory.O, 1.0m),
            // pro — equivalente a "Plus" del doc: ×3.0 default, I y J a ×5 (más volumen/templates).
            (pro, RateLimitCategory.F, 3.0m),
            (pro, RateLimitCategory.G, 3.0m),
            (pro, RateLimitCategory.H, 3.0m),
            (pro, RateLimitCategory.I, 5.0m),
            (pro, RateLimitCategory.J, 5.0m),
            (pro, RateLimitCategory.K, 3.0m),
            (pro, RateLimitCategory.L, 3.0m),
            (pro, RateLimitCategory.M, 1.0m),
            (pro, RateLimitCategory.N, 1.0m),
            (pro, RateLimitCategory.O, 3.0m),
            // enterprise — ×10.0 default, K a ×20 (envío) y H a ×15 (búsqueda).
            (enterprise, RateLimitCategory.F, 10.0m),
            (enterprise, RateLimitCategory.G, 10.0m),
            (enterprise, RateLimitCategory.H, 15.0m),
            (enterprise, RateLimitCategory.I, 10.0m),
            (enterprise, RateLimitCategory.J, 10.0m),
            (enterprise, RateLimitCategory.K, 20.0m),
            (enterprise, RateLimitCategory.L, 10.0m),
            (enterprise, RateLimitCategory.M, 1.0m),
            (enterprise, RateLimitCategory.N, 1.0m),
            (enterprise, RateLimitCategory.O, 10.0m),
        ];

        return rows.Select(
                (row, index) => PlanRateLimit.Seed(SeedId(index), row.Plan, row.Category, row.Multiplier).Value
            )
            .ToArray();
    }

    private static Guid SeedId(int index) => new($"c3000000-0000-0000-0000-{index:D12}");
}
