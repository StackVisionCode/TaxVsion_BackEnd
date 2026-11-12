using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralCodeShareConfiguration : IEntityTypeConfiguration<ReferralCodeShare>
{
    public void Configure(EntityTypeBuilder<ReferralCodeShare> builder)
    {
        builder.ToTable("ReferralCodeShares", GrowthSchemas.Referrals);
        builder.HasKey(share => share.Id);

        builder.Property(share => share.TenantId).IsRequired();
        builder.Property(share => share.ReferralCodeId).IsRequired();
        builder.Property(share => share.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(share => share.SharedByUserId).IsRequired();
        builder.Property(share => share.SharedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(share => share.RecipientHint).HasMaxLength(200);

        // Restrict: los envíos se conservan aunque el código se revoque (los registros que produjeron
        // conservan su premio).
        builder
            .HasOne<ReferralCode>()
            .WithMany()
            .HasForeignKey(share => share.ReferralCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(share => new { share.TenantId, share.SharedAtUtc })
            .HasDatabaseName("IX_ReferralCodeShares_TenantId_SharedAtUtc");
        builder
            .HasIndex(share => new { share.ReferralCodeId, share.SharedAtUtc })
            .HasDatabaseName("IX_ReferralCodeShares_ReferralCodeId_SharedAtUtc");
    }
}
