using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class EmailCampaignConfiguration : IEntityTypeConfiguration<EmailCampaign>
{
    public void Configure(EntityTypeBuilder<EmailCampaign> builder)
    {
        builder.ToTable("EmailCampaigns");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(c => c.SubjectTemplate).HasMaxLength(500);
        builder.Property(c => c.AllowedVariablesJson).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();

        builder.HasMany(c => c.Recipients).WithOne().HasForeignKey(r => r.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Recipients).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.TenantId, c.Status });
        builder.HasIndex(c => new { c.Status, c.ScheduledAtUtc });
    }
}

public sealed class EmailCampaignRecipientConfiguration : IEntityTypeConfiguration<EmailCampaignRecipient>
{
    public void Configure(EntityTypeBuilder<EmailCampaignRecipient> builder)
    {
        builder.ToTable("EmailCampaignRecipients");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.CampaignId).IsRequired();
        builder.Property(r => r.Address).HasMaxLength(320).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.VariablesJson).IsRequired();
        builder.HasIndex(r => r.CampaignId);
    }
}
