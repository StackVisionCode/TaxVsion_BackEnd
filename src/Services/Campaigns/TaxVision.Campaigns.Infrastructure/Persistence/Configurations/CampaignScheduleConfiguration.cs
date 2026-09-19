using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class CampaignScheduleConfiguration : IEntityTypeConfiguration<CampaignSchedule>
{
    public void Configure(EntityTypeBuilder<CampaignSchedule> builder)
    {
        builder.ToTable("CampaignSchedules");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.CampaignId).IsRequired();
        builder.Property(s => s.Kind).HasConversion<int>().IsRequired();
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();
        builder.Property(s => s.NextFireAtUtc);
        builder.Property(s => s.IntervalMinutes);
        builder.Property(s => s.ContactListIdsCsv).HasMaxLength(4000).IsRequired();
        builder.Property(s => s.IncludeCustomers).IsRequired();
        builder.Property(s => s.LastFiredAtUtc);
        builder.Property(s => s.ActiveRunId);
        builder.Property(s => s.LeaseToken);
        builder.Property(s => s.LeasedUntilUtc);
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        // Scan del claim: schedules Active vencidos por NextFireAtUtc.
        builder.HasIndex(s => new { s.Status, s.NextFireAtUtc }).HasDatabaseName("IX_CampaignSchedules_Status_NextFireAtUtc");
        builder.HasIndex(s => new { s.TenantId, s.CampaignId }).HasDatabaseName("IX_CampaignSchedules_TenantId_CampaignId");
        builder.HasIndex(s => s.ActiveRunId).HasDatabaseName("IX_CampaignSchedules_ActiveRunId");
    }
}
