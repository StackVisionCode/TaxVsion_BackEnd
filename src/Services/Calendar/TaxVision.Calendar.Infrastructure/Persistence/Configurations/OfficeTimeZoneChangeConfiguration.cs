using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class OfficeTimeZoneChangeConfiguration : IEntityTypeConfiguration<OfficeTimeZoneChange>
{
    public void Configure(EntityTypeBuilder<OfficeTimeZoneChange> builder)
    {
        builder.ToTable("OfficeTimeZoneChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.FromTimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.ToTimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();

        // Un evento por versión de la oficina; la búsqueda del pendiente entra por oficina y estado.
        builder.HasIndex(c => new { c.TenantId, c.TenantVersion }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.Status });
    }
}
