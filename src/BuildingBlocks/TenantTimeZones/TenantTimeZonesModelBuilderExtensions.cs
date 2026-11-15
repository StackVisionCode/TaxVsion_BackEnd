using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.TenantTimeZones;

public static class TenantTimeZonesModelBuilderExtensions
{
    /// <summary>Mapea la proyección a <c>TenantTimeZoneProjections</c> en el schema del servicio. Se llama en OnModelCreating.</summary>
    public static ModelBuilder ApplyTenantTimeZoneProjection(this ModelBuilder modelBuilder, string? schema = null)
    {
        modelBuilder.Entity<TenantTimeZoneProjection>(b =>
        {
            b.ToTable("TenantTimeZoneProjections", schema);
            b.HasKey(p => p.TenantId);
            b.Property(p => p.TenantId).ValueGeneratedNever();
            b.Property(p => p.TimeZoneId).HasMaxLength(100).IsRequired();
            b.Property(p => p.SourceVersion);
            b.Property(p => p.SourceUpdatedAtUtc).IsRequired();
        });
        return modelBuilder;
    }
}
