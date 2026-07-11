using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Templates;
using TaxVision.Signature.Domain.Templates.ValueObjects;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class TemplateSignerSlotConfiguration : IEntityTypeConfiguration<TemplateSignerSlot>
{
    public void Configure(EntityTypeBuilder<TemplateSignerSlot> builder)
    {
        builder.ToTable("TemplateSignerSlots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.SignatureTemplateId).IsRequired();
        builder.Property(s => s.Order).IsRequired();
        builder.Property(s => s.DefaultLanguage).IsRequired().HasMaxLength(2);

        builder.OwnsOne(
            s => s.Role,
            role =>
            {
                role.Property(r => r.Value).HasColumnName("Role").HasMaxLength(TemplateSlotRole.MaxLength).IsRequired();
            }
        );

        builder.HasIndex(s => new { s.SignatureTemplateId, s.Order }).IsUnique();
    }
}
