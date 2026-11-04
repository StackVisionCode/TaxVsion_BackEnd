using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

/// <summary>Constancias de traspaso/pausa de agendados por retiro. Solo se agregan; sin FK (el agendado puede borrarse con su campaña).</summary>
public sealed class CampaignScheduleHandoverConfiguration : IEntityTypeConfiguration<CampaignScheduleHandover>
{
    public void Configure(EntityTypeBuilder<CampaignScheduleHandover> builder)
    {
        builder.ToTable("CampaignScheduleHandovers");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.TenantId).IsRequired();
        builder.Property(h => h.ScheduleId).IsRequired();
        builder.Property(h => h.CampaignId).IsRequired();
        builder.Property(h => h.FromUserId).IsRequired();
        builder.Property(h => h.Outcome).HasConversion<int>().IsRequired();
        builder.Property(h => h.OccurredAtUtc).IsRequired();

        // "¿Ya se pausó por el retiro de X?" y el historial por agendado.
        builder
            .HasIndex(h => new { h.TenantId, h.FromUserId })
            .HasDatabaseName("IX_CampaignScheduleHandovers_TenantId_FromUserId");
        builder
            .HasIndex(h => new { h.TenantId, h.ScheduleId })
            .HasDatabaseName("IX_CampaignScheduleHandovers_TenantId_ScheduleId");
    }
}
