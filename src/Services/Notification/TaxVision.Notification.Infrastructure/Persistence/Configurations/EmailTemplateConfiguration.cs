using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Emailing.Templates;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("EmailTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.TemplateKey).HasMaxLength(128).IsRequired();
        builder.Property(t => t.Subject).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.Category).HasMaxLength(64);
        builder.Property(t => t.VariablesJson).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        // Una plantilla por (Scope, TenantId, Key). HasFilter(null) evita el filtro automático de EF
        // "[TenantId] IS NOT NULL" que dejaría las plantillas System (TenantId NULL) SIN unicidad.
        builder
            .HasIndex(t => new
            {
                t.Scope,
                t.TenantId,
                t.TemplateKey,
            })
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(t => new { t.TenantId, t.Scope });
    }
}

public sealed class EmailTemplateVersionConfiguration : IEntityTypeConfiguration<EmailTemplateVersion>
{
    public void Configure(EntityTypeBuilder<EmailTemplateVersion> builder)
    {
        builder.ToTable("EmailTemplateVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.SubjectTemplate).HasMaxLength(500).IsRequired();
        builder.Property(v => v.HtmlStorageKey).HasMaxLength(512).IsRequired();
        builder.Property(v => v.DesignStorageKey).HasMaxLength(512);
        builder.Property(v => v.PreviewStorageKey).HasMaxLength(512);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(v => v.CreatedAtUtc).IsRequired();

        builder.HasIndex(v => new { v.TemplateId, v.VersionNumber }).IsUnique();
    }
}
