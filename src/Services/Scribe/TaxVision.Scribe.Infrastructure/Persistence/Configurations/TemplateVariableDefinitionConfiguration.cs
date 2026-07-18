using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class TemplateVariableDefinitionConfiguration : IEntityTypeConfiguration<TemplateVariableDefinition>
{
    public void Configure(EntityTypeBuilder<TemplateVariableDefinition> builder)
    {
        builder.ToTable("TemplateVariableDefinitions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.Required).IsRequired();
        builder.Property(d => d.DefaultValue).HasMaxLength(2000);
        builder.Property(d => d.Description).HasMaxLength(500);

        builder.HasIndex(d => d.EmailTemplateVersionId);
    }
}
