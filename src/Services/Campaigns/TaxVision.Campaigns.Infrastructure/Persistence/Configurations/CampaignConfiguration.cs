using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.CreatedByUserId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(Campaign.MaxNameLength).IsRequired();
        builder.Property(c => c.Subject).HasMaxLength(Campaign.MaxSubjectLength);
        builder.Property(c => c.Message).HasMaxLength(Campaign.MaxMessageLength).IsRequired();
        builder.Property(c => c.Channels).HasConversion<int>().IsRequired();
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Status }).HasDatabaseName("IX_Campaigns_TenantId_Status");
        builder.HasIndex(c => new { c.TenantId, c.CreatedAtUtc }).HasDatabaseName("IX_Campaigns_TenantId_CreatedAtUtc");

        builder.HasMany(c => c.Senders).WithOne().HasForeignKey(s => s.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Campaign.Senders))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class CampaignSenderSelectionConfiguration : IEntityTypeConfiguration<CampaignSenderSelection>
{
    public void Configure(EntityTypeBuilder<CampaignSenderSelection> builder)
    {
        builder.ToTable("CampaignSenderSelections");
        builder.HasKey(s => s.Id);
        // Id generado en dominio (guardrail 10): sin esto EF haría UPDATE en vez de INSERT al agregar hijos.
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.CampaignId).IsRequired();
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.Channel).HasConversion<int>().IsRequired();
        builder.Property(s => s.SenderProfileId).IsRequired();

        builder
            .HasIndex(s => new { s.CampaignId, s.Channel })
            .IsUnique()
            .HasDatabaseName("UX_CampaignSenderSelections_CampaignId_Channel");
    }
}
