using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Catalog.Domain.Items;

namespace TaxVision.Catalog.Infrastructure.Persistence.Configurations;

public sealed class CatalogItemAttributeConfiguration : IEntityTypeConfiguration<CatalogItemAttribute>
{
    public void Configure(EntityTypeBuilder<CatalogItemAttribute> builder)
    {
        builder.ToTable("CatalogItemAttributes");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.CatalogItemId).IsRequired();
        builder.Property(a => a.Key).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Value).HasMaxLength(4000).IsRequired();
        builder.Property(a => a.ValueType).HasMaxLength(20);

        builder.HasIndex(a => new { a.CatalogItemId, a.Key });
    }
}
