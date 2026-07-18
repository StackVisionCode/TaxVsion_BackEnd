using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("EmailTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder
            .Property(t => t.TemplateKey)
            .HasConversion(key => key.Value, value => TemplateKey.Create(value).Value)
            .HasColumnName("TemplateKey")
            .HasMaxLength(TemplateKey.MaxLength)
            .IsRequired();

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        // Filtro null explícito: sin esto, EF podría heredar un filtro de índice que dejaría
        // las plantillas System (TenantId NULL) sin garantía de unicidad.
        builder
            .HasIndex(t => new
            {
                t.Scope,
                t.TenantId,
                t.TemplateKey,
            })
            .IsUnique()
            .HasFilter(null);

        builder
            .HasMany(t => t.Versions)
            .WithOne()
            .HasForeignKey(v => v.EmailTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
