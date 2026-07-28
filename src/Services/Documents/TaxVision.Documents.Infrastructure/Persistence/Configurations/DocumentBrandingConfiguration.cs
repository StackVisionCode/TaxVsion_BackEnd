using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Infrastructure.Persistence.Configurations;

public sealed class DocumentBrandingConfiguration : IEntityTypeConfiguration<DocumentBranding>
{
    public void Configure(EntityTypeBuilder<DocumentBranding> builder)
    {
        builder.ToTable("DocumentBrandings", DocumentsSchemas.Documents);
        builder.HasKey(b => b.Id);

        builder.Property(b => b.TenantId).IsRequired();

        builder.Property(b => b.DisplayName).HasMaxLength(DocumentBranding.MaxDisplayNameLength);
        builder.Property(b => b.LogoDataUri); // nvarchar(max): data-URI del logo embebido
        builder.Property(b => b.BrandColorHex).HasMaxLength(7);
        builder.Property(b => b.FooterText).HasMaxLength(DocumentBranding.MaxFooterLength);

        builder.Property(b => b.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(b => b.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(b => b.RowVersion).IsRowVersion();

        // Uno por tenant.
        builder.HasIndex(b => b.TenantId).IsUnique().HasDatabaseName("UX_DocumentBrandings_Tenant");
    }
}
