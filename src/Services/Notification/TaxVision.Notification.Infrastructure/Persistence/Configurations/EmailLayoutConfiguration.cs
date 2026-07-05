using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Emailing.Layouts;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class EmailLayoutConfiguration : IEntityTypeConfiguration<EmailLayout>
{
    public void Configure(EntityTypeBuilder<EmailLayout> builder)
    {
        builder.ToTable("EmailLayouts");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(l => l.LayoutName).HasMaxLength(128).IsRequired();
        builder.Property(l => l.HtmlStorageKey).HasMaxLength(512);
        builder.Property(l => l.DesignStorageKey).HasMaxLength(512);
        builder.Property(l => l.PreviewStorageKey).HasMaxLength(512);
        builder.Property(l => l.CreatedAtUtc).IsRequired();

        // A lo sumo un layout default por (Scope, TenantId) — invariante forzada por la BD.
        builder.HasIndex(l => new { l.Scope, l.TenantId }).HasFilter("[IsDefault] = 1").IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.Scope });
    }
}
