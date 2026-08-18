using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Configurations;

public sealed class TermsVersionConfiguration : IEntityTypeConfiguration<TermsVersion>
{
    public void Configure(EntityTypeBuilder<TermsVersion> builder)
    {
        builder.ToTable("TermsVersions");
        builder.HasKey(version => version.Id);

        builder.Property(version => version.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(version => version.Version).HasMaxLength(64).IsRequired();
        builder.Property(version => version.ContentFileId);
        builder.Property(version => version.ContentUri).HasMaxLength(2048);
        builder.Property(version => version.ContentHash).HasMaxLength(64);
        builder.Property(version => version.EffectiveFromUtc).IsRequired();
        builder.Property(version => version.Locale).HasMaxLength(16).IsRequired();
        builder.Property(version => version.CreatedAtUtc).IsRequired();
        builder.Property(version => version.CreatedByUserId).IsRequired();

        builder.HasIndex(version => new
        {
            version.Kind,
            version.Locale,
            version.EffectiveFromUtc,
        });
    }
}
