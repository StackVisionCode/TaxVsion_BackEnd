using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class EventTemplateMappingConfiguration : IEntityTypeConfiguration<EventTemplateMapping>
{
    public void Configure(EntityTypeBuilder<EventTemplateMapping> builder)
    {
        builder.ToTable("EventTemplateMappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder
            .Property(m => m.EventKey)
            .HasConversion(key => key.Value, value => EventKey.Create(value).Value)
            .HasColumnName("EventKey")
            .HasMaxLength(EventKey.MaxLength)
            .IsRequired();

        builder
            .Property(m => m.TemplateKey)
            .HasConversion(key => key.Value, value => TemplateKey.Create(value).Value)
            .HasColumnName("TemplateKey")
            .HasMaxLength(TemplateKey.MaxLength)
            .IsRequired();

        builder
            .Property(m => m.Locale)
            .HasConversion(
                locale => locale == null ? null : locale.Value,
                value => value == null ? null : Locale.Create(value).Value
            )
            .HasColumnName("Locale")
            .HasMaxLength(Locale.MaxLength);

        builder.Property(m => m.Priority).IsRequired();
        builder.Property(m => m.Enabled).IsRequired();
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        // Filtro null explícito: sin esto, EF podría heredar un filtro de índice que dejaría
        // los mappings System (TenantId NULL) sin garantía de unicidad.
        builder
            .HasIndex(m => new
            {
                m.Scope,
                m.TenantId,
                m.EventKey,
                m.Locale,
            })
            .IsUnique()
            .HasFilter(null);

        builder.HasIndex(m => new { m.EventKey, m.Enabled });
    }
}
