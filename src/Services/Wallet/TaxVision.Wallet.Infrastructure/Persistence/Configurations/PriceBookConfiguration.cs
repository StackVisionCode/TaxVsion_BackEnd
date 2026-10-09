using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Infrastructure.Persistence.Configurations;

public sealed class PriceBookVersionConfiguration : IEntityTypeConfiguration<PriceBookVersion>
{
    // Guids fijos para el seed de la versión 1 (default). No tenant-owned (catálogo global).
    internal static readonly Guid SeedVersionId = new("b2000000-0000-0000-0000-000000000001");
    private static readonly DateTime SeedEffective = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<PriceBookVersion> builder)
    {
        builder.ToTable("PriceBookVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Version).IsRequired();
        builder.Property(v => v.EffectiveFromUtc).IsRequired();
        builder.Property(v => v.CreatedByUserId).IsRequired();
        builder.Property(v => v.CreatedAtUtc).IsRequired();
        builder.HasIndex(v => v.Version).IsUnique();

        builder
            .HasMany(v => v.Rules)
            .WithOne()
            .HasForeignKey(r => r.PriceBookVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Rules).HasField("_rules").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Seed: versión 1 vigente (default). Email/Push 1¢ (10 000 micros), SMS/WhatsApp 5¢ (50 000 micros).
        builder.HasData(
            new
            {
                Id = SeedVersionId,
                Version = 1,
                EffectiveFromUtc = SeedEffective,
                CreatedByUserId = Guid.Empty,
                CreatedAtUtc = SeedEffective,
            }
        );
    }
}

public sealed class PriceRuleConfiguration : IEntityTypeConfiguration<PriceRule>
{
    public void Configure(EntityTypeBuilder<PriceRule> builder)
    {
        builder.ToTable("PriceRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.PriceBookVersionId).IsRequired();
        builder.Property(r => r.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.UnitPriceMicros).IsRequired();
        builder.HasIndex(r => new { r.PriceBookVersionId, r.Channel }).IsUnique();

        var v = PriceBookVersionConfiguration.SeedVersionId;
        builder.HasData(
            new
            {
                Id = new Guid("b2000000-0000-0000-0001-000000000001"),
                PriceBookVersionId = v,
                Channel = PriceChannel.Email,
                UnitPriceMicros = 10_000L,
            },
            new
            {
                Id = new Guid("b2000000-0000-0000-0001-000000000002"),
                PriceBookVersionId = v,
                Channel = PriceChannel.Sms,
                UnitPriceMicros = 50_000L,
            },
            new
            {
                Id = new Guid("b2000000-0000-0000-0001-000000000003"),
                PriceBookVersionId = v,
                Channel = PriceChannel.Push,
                UnitPriceMicros = 10_000L,
            },
            new
            {
                Id = new Guid("b2000000-0000-0000-0001-000000000004"),
                PriceBookVersionId = v,
                Channel = PriceChannel.WhatsApp,
                UnitPriceMicros = 50_000L,
            }
        );
    }
}
