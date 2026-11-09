using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

internal sealed class StartingSoonNoticeConfiguration : IEntityTypeConfiguration<StartingSoonNotice>
{
    public void Configure(EntityTypeBuilder<StartingSoonNotice> builder)
    {
        builder.ToTable("AppointmentStartingSoonNotices");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.OccurrenceTargetId).IsRequired();
        builder.Property(n => n.StartUtc).IsRequired();
        builder.Property(n => n.PublishedAtUtc).IsRequired();

        // Una ocurrencia, un aviso: lo que no entra aquí ya lo avisó otra réplica o una corrida anterior.
        builder
            .HasIndex(
                n => new
                {
                    n.TenantId,
                    n.OccurrenceTargetId,
                    n.StartUtc,
                },
                "UX_AppointmentStartingSoonNotices_Occurrence"
            )
            .IsUnique();
    }
}
