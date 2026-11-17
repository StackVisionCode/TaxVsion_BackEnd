using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Growth.Infrastructure.Persistence.Referrals;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferredOfficeProjectionConfiguration : IEntityTypeConfiguration<ReferredOfficeProjection>
{
    public void Configure(EntityTypeBuilder<ReferredOfficeProjection> builder)
    {
        builder.ToTable("ReferredOffices", GrowthSchemas.Referrals);
        builder.HasKey(office => office.Id);

        builder.Property(office => office.TenantId).IsRequired();
        builder.Property(office => office.Name).HasMaxLength(ReferredOfficeProjection.NameMaxLength).IsRequired();
        builder.Property(office => office.NameUpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(office => office.DisplayName).HasMaxLength(ReferredOfficeProjection.NameMaxLength);
        builder.Property(office => office.ProfileVersion).IsRequired();
        builder.Property(office => office.OwnerSubjectId).HasMaxLength(64);
        builder.Property(office => office.OwnerEmailDisposable).IsRequired();

        builder.HasIndex(office => office.TenantId).IsUnique().HasDatabaseName("UX_ReferredOffices_TenantId");
        builder
            .HasIndex(office => office.OnboardingId)
            .HasFilter("[OnboardingId] IS NOT NULL")
            .HasDatabaseName("IX_ReferredOffices_OnboardingId");
    }
}
