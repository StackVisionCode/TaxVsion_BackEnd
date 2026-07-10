using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignatureTemplateConfiguration : IEntityTypeConfiguration<SignatureTemplate>
{
    public void Configure(EntityTypeBuilder<SignatureTemplate> builder)
    {
        builder.ToTable("SignatureTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.CreatedByUserId).IsRequired();
        builder.Property(t => t.Title).IsRequired().HasMaxLength(SignatureTemplate.MaxTitleLength);
        builder.Property(t => t.Description).HasMaxLength(SignatureTemplate.MaxDescriptionLength);
        builder.Property(t => t.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.DefaultTokenExpirationHours).IsRequired();
        builder.Property(t => t.RequiresSequentialSigning).IsRequired();
        builder.Property(t => t.RequiresConsent).IsRequired();
        builder.Property(t => t.GenerateCertificate).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();
        builder.Property(t => t.PublishedAtUtc);
        builder.Property(t => t.ArchivedAtUtc);

        builder.HasIndex(t => new { t.TenantId, t.Status });
        builder.HasIndex(t => new { t.TenantId, t.Category });

        builder
            .HasMany(t => t.Slots)
            .WithOne()
            .HasForeignKey(s => s.SignatureTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .Metadata.FindNavigation(nameof(SignatureTemplate.Slots))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(t => t.Fields)
            .WithOne()
            .HasForeignKey(f => f.SignatureTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .Metadata.FindNavigation(nameof(SignatureTemplate.Fields))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
