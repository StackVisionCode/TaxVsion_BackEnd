using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class PreparerFieldConfiguration : IEntityTypeConfiguration<PreparerField>
{
    public void Configure(EntityTypeBuilder<PreparerField> builder)
    {
        builder.ToTable("PreparerFields");
        builder.HasKey(field => field.Id);
        builder.Property(field => field.Id).ValueGeneratedNever();

        builder.Property(field => field.SignatureRequestId).IsRequired();
        builder.Property(field => field.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(field => field.Label).HasMaxLength(PreparerField.MaxLabelLength);
        builder.Property(field => field.CreatedAtUtc).IsRequired();

        builder.OwnsOne(
            field => field.Position,
            position =>
            {
                position.Property(p => p.Page).HasColumnName("Position_Page").IsRequired();
                position.Property(p => p.X).HasColumnName("Position_X").IsRequired();
                position.Property(p => p.Y).HasColumnName("Position_Y").IsRequired();
                position.Property(p => p.Width).HasColumnName("Position_Width").IsRequired();
                position.Property(p => p.Height).HasColumnName("Position_Height").IsRequired();
            }
        );

        builder.HasIndex(field => field.SignatureRequestId);
    }
}
