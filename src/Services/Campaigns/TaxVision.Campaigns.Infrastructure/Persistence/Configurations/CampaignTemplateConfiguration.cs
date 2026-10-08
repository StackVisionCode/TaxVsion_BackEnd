using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Templates;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class CampaignTemplateConfiguration : IEntityTypeConfiguration<CampaignTemplate>
{
    public void Configure(EntityTypeBuilder<CampaignTemplate> builder)
    {
        builder.ToTable("CampaignTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.CreatedByUserId).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(CampaignTemplate.MaxNameLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(CampaignTemplate.MaxDescriptionLength);
        builder.Property(t => t.Channels).HasConversion<int>().IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();

        builder
            .HasIndex(t => new { t.TenantId, t.CreatedAtUtc })
            .HasDatabaseName("IX_CampaignTemplates_TenantId_CreatedAtUtc");

        builder.HasMany(t => t.Contents).WithOne().HasForeignKey(c => c.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder
            .Metadata.FindNavigation(nameof(CampaignTemplate.Contents))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class CampaignTemplateContentConfiguration : IEntityTypeConfiguration<CampaignTemplateContent>
{
    public void Configure(EntityTypeBuilder<CampaignTemplateContent> builder)
    {
        builder.ToTable("CampaignTemplateContents");
        builder.HasKey(c => c.Id);
        // Id generado en dominio (guardrail 10): sin esto EF haría UPDATE en vez de INSERT al agregar hijos.
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.TemplateId).IsRequired();
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Channel).HasConversion<int>().IsRequired();
        builder.Property(c => c.Subject).HasMaxLength(CampaignTemplate.MaxSubjectLength);
        builder.Property(c => c.Title).HasMaxLength(CampaignTemplate.MaxTitleLength);
        builder.Property(c => c.Body).HasMaxLength(CampaignTemplate.MaxBodyLength).IsRequired();

        builder
            .HasIndex(c => new { c.TemplateId, c.Channel })
            .IsUnique()
            .HasDatabaseName("UX_CampaignTemplateContents_TemplateId_Channel");
    }
}
