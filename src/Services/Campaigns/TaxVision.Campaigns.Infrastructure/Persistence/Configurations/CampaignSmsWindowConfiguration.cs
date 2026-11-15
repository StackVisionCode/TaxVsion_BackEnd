using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Settings;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class CampaignSmsWindowConfiguration : IEntityTypeConfiguration<CampaignSmsWindow>
{
    public void Configure(EntityTypeBuilder<CampaignSmsWindow> builder)
    {
        builder.ToTable("CampaignSmsWindows");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.TenantId).IsRequired();
        builder.Property(w => w.Opens).IsRequired();
        builder.Property(w => w.Closes).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedByUserId).IsRequired();

        // Una ventana por oficina.
        builder.HasIndex(w => w.TenantId).IsUnique().HasDatabaseName("UX_CampaignSmsWindows_TenantId");
    }
}
