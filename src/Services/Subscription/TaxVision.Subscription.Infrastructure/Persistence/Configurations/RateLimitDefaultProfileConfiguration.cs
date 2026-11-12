using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.RateLimiting;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

/// <summary>D12: el perfil por defecto, sembrado por migración con los multiplicadores de Enterprise.</summary>
public sealed class RateLimitDefaultProfileConfiguration : IEntityTypeConfiguration<RateLimitDefaultProfile>
{
    private static readonly DateTime SeededAtUtc = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<RateLimitDefaultProfile> builder)
    {
        builder.ToTable("RateLimitDefaultProfiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.RowVersion).IsRowVersion();
        builder.Property(profile => profile.UpdatedAtUtc).IsRequired();
        builder.HasData(
            new
            {
                Id = RateLimitDefaultProfile.PlatformId,
                UpdatedAtUtc = SeededAtUtc,
                UpdatedBy = Guid.Empty,
            }
        );

        builder.OwnsMany(
            profile => profile.Entries,
            entries =>
            {
                entries.ToTable("RateLimitDefaultProfileEntries");
                entries.WithOwner().HasForeignKey("ProfileId");
                entries.Property<Guid>("ProfileId");
                entries.HasKey("ProfileId", nameof(RateLimitProfileEntry.Category));
                entries.Property(entry => entry.Category).HasConversion<string>().HasMaxLength(5);
                entries.Property(entry => entry.Multiplier).HasPrecision(9, 4);
                entries.HasData(
                    RateLimitDefaultProfile.ScalableCategories.Select(category => new
                    {
                        ProfileId = RateLimitDefaultProfile.PlatformId,
                        Category = category,
                        Multiplier = RateLimitDefaultProfile.EnterpriseDefaults[category],
                    })
                );
            }
        );
        builder.Navigation(profile => profile.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
