using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class EmailTemplateVersionConfiguration : IEntityTypeConfiguration<EmailTemplateVersion>
{
    public void Configure(EntityTypeBuilder<EmailTemplateVersion> builder)
    {
        builder.ToTable("EmailTemplateVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(v => v.Subject).HasMaxLength(500).IsRequired();
        builder.Property(v => v.HtmlStorageKey).HasMaxLength(500).IsRequired();
        builder.Property(v => v.HtmlFileId).IsRequired();
        builder.Property(v => v.TextStorageKey).HasMaxLength(500);
        builder.Property(v => v.TextFileId);
        builder.Property(v => v.DesignJsonStorageKey).HasMaxLength(500);
        builder.Property(v => v.DesignJsonFileId);
        builder.Property(v => v.PreviewImageStorageKey).HasMaxLength(500);
        builder.Property(v => v.PreviewImageFileId);
        builder.Property(v => v.LayoutId).IsRequired();
        builder.Property(v => v.LayoutVersionNumber).IsRequired();
        builder.Property(v => v.CreatedAtUtc).IsRequired();

        builder.HasIndex(v => new { v.EmailTemplateId, v.VersionNumber }).IsUnique();

        // Único filtrado: solo una versión Published a la vez por template (invariante del aggregate).
        builder.HasIndex(v => new { v.EmailTemplateId, v.Status }).IsUnique().HasFilter("[Status] = 'Published'");

        builder
            .HasMany(v => v.VariableDefinitions)
            .WithOne()
            .HasForeignKey(d => d.EmailTemplateVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
