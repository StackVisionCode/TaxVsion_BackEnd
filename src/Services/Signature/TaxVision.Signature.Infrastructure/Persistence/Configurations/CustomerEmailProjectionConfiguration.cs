using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

internal sealed class CustomerEmailProjectionConfiguration : IEntityTypeConfiguration<CustomerEmailProjection>
{
    public void Configure(EntityTypeBuilder<CustomerEmailProjection> builder)
    {
        builder.ToTable("CustomerEmailProjections");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.CustomerId).IsRequired();
        builder.Property(p => p.NormalizedEmail).IsRequired().HasMaxLength(320);
        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.IsArchived).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.CustomerId }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.NormalizedEmail });
    }
}
