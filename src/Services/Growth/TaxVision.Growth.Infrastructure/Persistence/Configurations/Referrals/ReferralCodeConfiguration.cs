using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Referrals;

public sealed class ReferralCodeConfiguration : IEntityTypeConfiguration<ReferralCode>
{
    public void Configure(EntityTypeBuilder<ReferralCode> builder)
    {
        builder.ToTable("ReferralCodes", GrowthSchemas.Referrals);
        builder.HasKey(code => code.Id);

        builder.Property(code => code.TenantId).IsRequired();
        builder.Property(code => code.ProgramId).IsRequired();
        builder.Property(code => code.TenantScopeId);
        builder.Property(code => code.OwnerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(code => code.OwnerId).IsRequired();
        builder.Property(code => code.CodeHash).HasColumnType("char(64)").IsFixedLength().IsRequired();
        builder.Property(code => code.DisplayPrefix).HasMaxLength(12).IsRequired();
        builder.Property(code => code.LastFour).HasColumnType("char(4)").IsRequired();
        builder.Property(code => code.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(code => code.ExpiresAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(code => code.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder
            .Property(code => code.PayloadFingerprint)
            .HasColumnType("char(64)")
            .IsFixedLength()
            .IsRequired();
        builder.Property(code => code.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(code => code.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(code => code.CreatedBy).IsRequired();
        builder.Property(code => code.UpdatedBy).IsRequired();
        builder.Property(code => code.RevokedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(code => code.RevocationReason).HasMaxLength(500);
        builder.Property(code => code.RowVersion).IsRowVersion();

        builder
            .HasOne<ReferralProgram>()
            .WithMany()
            .HasForeignKey(code => code.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(code => new { code.ProgramId, code.CodeHash })
            .IsUnique()
            .HasDatabaseName("UX_ReferralCodes_ProgramId_CodeHash");
        builder
            .HasIndex(code => new
            {
                code.ProgramId,
                code.OwnerType,
                code.OwnerId,
            })
            .HasFilter("[Status] = N'Active'")
            .IsUnique()
            .HasDatabaseName("UX_ReferralCodes_ActiveOwner");
        builder
            .HasIndex(code => new { code.TenantId, code.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ReferralCodes_TenantId_IdempotencyKey");
    }
}
