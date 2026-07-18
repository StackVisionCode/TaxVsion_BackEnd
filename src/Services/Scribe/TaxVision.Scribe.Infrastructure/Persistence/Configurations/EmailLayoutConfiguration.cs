using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class EmailLayoutConfiguration : IEntityTypeConfiguration<EmailLayout>
{
    public void Configure(EntityTypeBuilder<EmailLayout> builder)
    {
        builder.ToTable("EmailLayouts");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder
            .Property(l => l.LayoutKey)
            .HasConversion(key => key.Value, value => LayoutKey.Create(value).Value)
            .HasColumnName("LayoutKey")
            .HasMaxLength(LayoutKey.MaxLength)
            .IsRequired();

        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(2000);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(l => l.CreatedAtUtc).IsRequired();

        // Filtro null explícito: sin esto, EF podría heredar un filtro de índice que dejaría
        // los layouts System (TenantId NULL) sin garantía de unicidad.
        builder
            .HasIndex(l => new
            {
                l.Scope,
                l.TenantId,
                l.LayoutKey,
            })
            .IsUnique()
            .HasFilter(null);

        builder.HasMany(l => l.Versions).WithOne().HasForeignKey(v => v.EmailLayoutId).OnDelete(DeleteBehavior.Cascade);
    }
}
