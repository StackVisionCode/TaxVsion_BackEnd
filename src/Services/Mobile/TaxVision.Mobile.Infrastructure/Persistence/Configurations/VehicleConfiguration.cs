using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable(
            "Vehicles",
            t =>
            {
                t.HasCheckConstraint(
                    "CK_Vehicles_CurrentMileage",
                    "[CurrentMileage] >= 0 AND [CurrentMileage] <= 999999999.99"
                );
                t.HasCheckConstraint("CK_Vehicles_Year", "[Year] IS NULL OR ([Year] >= 1886 AND [Year] <= 2100)");
            }
        );
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new
        {
            x.TenantId,
            x.CustomerId,
            x.Id,
        });
        builder
            .HasIndex(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.IsDefault,
            })
            .IsUnique()
            .HasFilter("[IsDefault] = 1");
        builder.Property<string>("PlateKey").HasMaxLength(32).HasComputedColumnSql("[Plate]", stored: true);
        builder.HasIndex("TenantId", "CustomerId", "PlateKey").IsUnique().HasFilter(null);
        builder.OwnsOne(
            x => x.Details,
            details =>
            {
                details.Property(x => x.Plate).HasColumnName("Plate").HasMaxLength(32).IsRequired();
                details.Property(x => x.Make).HasColumnName("Make").HasMaxLength(100);
                details.Property(x => x.Model).HasColumnName("Model").HasMaxLength(100);
                details.Property(x => x.Year).HasColumnName("Year");
                details.Property(x => x.Color).HasColumnName("Color").HasMaxLength(50);
                details.Property(x => x.Photo).HasColumnName("Photo").HasMaxLength(2048);
                details.Property(x => x.CurrentMileage).HasColumnName("CurrentMileage").HasPrecision(11, 2);
            }
        );
        builder.Navigation(x => x.Details).IsRequired();
    }
}
