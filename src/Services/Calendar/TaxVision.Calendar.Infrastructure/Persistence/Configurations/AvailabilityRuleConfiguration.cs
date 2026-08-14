using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class AvailabilityRuleConfiguration : IEntityTypeConfiguration<AvailabilityRule>
{
    public void Configure(EntityTypeBuilder<AvailabilityRule> builder)
    {
        builder.ToTable("AvailabilityRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.StartTime).IsRequired();
        builder.Property(r => r.EndTime).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();

        // Mascara de dias a un entero: se filtra en SQL y no necesita tabla hija.
        builder.Property(r => r.Days).HasConversion<int>().IsRequired();

        builder
            .Property(r => r.TimeZone)
            .HasColumnName("TimeZoneId")
            .HasConversion(zone => zone.Id, value => CalendarTimeZone.Create(value).Value)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(r => new
        {
            r.TenantId,
            r.UserId,
            r.IsActive,
        });
    }
}
