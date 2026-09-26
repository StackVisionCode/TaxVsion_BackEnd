using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class TemplatePreparerFieldConfiguration : IEntityTypeConfiguration<TemplatePreparerField>
{
    public void Configure(EntityTypeBuilder<TemplatePreparerField> builder)
    {
        builder.ToTable("TemplatePreparerFields");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.SignatureTemplateId).IsRequired();
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(f => f.Label).HasMaxLength(TemplatePreparerField.MaxLabelLength);

        builder.OwnsOne(
            f => f.Position,
            pos =>
            {
                pos.Property(p => p.Page).HasColumnName("Position_Page").IsRequired();
                pos.Property(p => p.X).HasColumnName("Position_X").IsRequired();
                pos.Property(p => p.Y).HasColumnName("Position_Y").IsRequired();
                pos.Property(p => p.Width).HasColumnName("Position_Width").IsRequired();
                pos.Property(p => p.Height).HasColumnName("Position_Height").IsRequired();
            }
        );

        builder.HasIndex(f => f.SignatureTemplateId);
    }
}
