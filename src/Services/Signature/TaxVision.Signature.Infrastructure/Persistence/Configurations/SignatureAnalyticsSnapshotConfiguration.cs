using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Analytics;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignatureAnalyticsSnapshotConfiguration : IEntityTypeConfiguration<SignatureAnalyticsSnapshot>
{
    public void Configure(EntityTypeBuilder<SignatureAnalyticsSnapshot> builder)
    {
        builder.ToTable("SignatureAnalyticsSnapshots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.Day).IsRequired();
        builder.Property(s => s.Category).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(s => s.RequestsCreated).IsRequired();
        builder.Property(s => s.RequestsSent).IsRequired();
        builder.Property(s => s.RequestsCanceled).IsRequired();
        builder.Property(s => s.RequestsExpired).IsRequired();
        builder.Property(s => s.RequestsCompleted).IsRequired();
        builder.Property(s => s.RequestsSealed).IsRequired();
        builder.Property(s => s.SignersSigned).IsRequired();
        builder.Property(s => s.SignersRejected).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        builder
            .HasIndex(s => new
            {
                s.TenantId,
                s.Day,
                s.Category,
            })
            .IsUnique();
    }
}
