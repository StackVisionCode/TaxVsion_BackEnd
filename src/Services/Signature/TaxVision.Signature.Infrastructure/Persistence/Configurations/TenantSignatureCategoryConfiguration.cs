using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Categories;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class TenantSignatureCategoryConfiguration : IEntityTypeConfiguration<TenantSignatureCategory>
{
    public void Configure(EntityTypeBuilder<TenantSignatureCategory> builder)
    {
        builder.ToTable("SignatureCategories");
        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name).HasMaxLength(TenantSignatureCategory.MaxNameLength).IsRequired();
        builder
            .Property(category => category.NormalizedName)
            .HasMaxLength(TenantSignatureCategory.MaxNameLength)
            .IsRequired();
        builder.Property(category => category.IsArchived).IsRequired();

        // Un nombre normalizado único por tenant (dedup a nivel de BD, incluye archivadas).
        builder.HasIndex(category => new { category.TenantId, category.NormalizedName }).IsUnique();
    }
}
