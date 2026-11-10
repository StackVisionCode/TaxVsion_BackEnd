using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class OfficeScheduleProjectionConfiguration : IEntityTypeConfiguration<OfficeScheduleProjection>
{
    public void Configure(EntityTypeBuilder<OfficeScheduleProjection> builder)
    {
        builder.ToTable("OfficeSchedules");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.TimeZoneId).HasMaxLength(64);
        builder.Property(p => p.HoursJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(p => p.ClosuresJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Ignore(p => p.Hours);
        builder.Ignore(p => p.Closures);

        // Una fila por oficina: si dos eventos la crean a la vez, el segundo reintenta y la encuentra.
        builder.HasIndex(p => p.TenantId).IsUnique();
    }
}
