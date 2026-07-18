using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Layouts;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class EmailLayoutVersionConfiguration : IEntityTypeConfiguration<EmailLayoutVersion>
{
    public void Configure(EntityTypeBuilder<EmailLayoutVersion> builder)
    {
        builder.ToTable("EmailLayoutVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(v => v.HtmlStorageKey).HasMaxLength(500).IsRequired();
        builder.Property(v => v.HtmlFileId).IsRequired();
        builder.Property(v => v.DesignJsonStorageKey).HasMaxLength(500);
        builder.Property(v => v.DesignJsonFileId);
        builder.Property(v => v.PreviewImageStorageKey).HasMaxLength(500);
        builder.Property(v => v.PreviewImageFileId);
        builder.Property(v => v.CreatedAtUtc).IsRequired();

        builder.HasIndex(v => new { v.EmailLayoutId, v.VersionNumber }).IsUnique();

        // Único filtrado: solo una versión Published a la vez por layout (invariante del aggregate).
        builder.HasIndex(v => new { v.EmailLayoutId, v.Status }).IsUnique().HasFilter("[Status] = 'Published'");
    }
}
