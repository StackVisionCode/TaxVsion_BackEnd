using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class TenantProfileRefConfiguration : IEntityTypeConfiguration<TenantProfileRef>
{
    public void Configure(EntityTypeBuilder<TenantProfileRef> builder)
    {
        builder.ToTable("TenantProfileRefs");
        builder.HasKey(r => r.TenantId);
        builder.Property(r => r.TenantId).ValueGeneratedNever();

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.SubDomain).HasMaxLength(100).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();
    }
}
