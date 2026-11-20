using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

public sealed class MileageRetentionPolicyConfiguration : IEntityTypeConfiguration<MileageRetentionPolicy>
{
    public void Configure(EntityTypeBuilder<MileageRetentionPolicy> b)
    {
        b.ToTable(
            "MobileMileageRetentionPolicies",
            t =>
                t.HasCheckConstraint(
                    "CK_MobileMileageRetentionPolicies_Days",
                    "[RetentionDays] IS NULL OR [RetentionDays] BETWEEN 1 AND 3650"
                )
        );
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.TenantId).IsUnique();
    }
}
