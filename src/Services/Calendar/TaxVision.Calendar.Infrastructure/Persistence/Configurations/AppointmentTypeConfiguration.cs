using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

public sealed class AppointmentTypeConfiguration : IEntityTypeConfiguration<AppointmentType>
{
    public void Configure(EntityTypeBuilder<AppointmentType> builder)
    {
        builder.ToTable("AppointmentTypes");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(AppointmentType.MaxNameLength).IsRequired();
        builder.Property(t => t.DefaultDuration).IsRequired();
        builder.Property(t => t.ColorHex).HasMaxLength(7).IsRequired();
        builder.Property(t => t.IsVirtual).IsRequired();
        builder.Property(t => t.BlocksOnConflict).IsRequired();
        builder.Property(t => t.DailyCap);
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        // Dos tipos activos con el mismo nombre en un tenant son un error de dedo; los desactivados
        // quedan fuera del indice porque su nombre puede reciclarse.
        builder
            .HasIndex(t => new { t.TenantId, t.Name })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_AppointmentTypes_TenantId_Name_Active");
    }
}
