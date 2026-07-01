using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(tenant => tenant.Id);

        builder.Property(tenant => tenant.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(tenant => tenant.SubDomain)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(tenant => tenant.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(tenant => tenant.DefaultTimeZoneId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(tenant => tenant.IsActive)
            .IsRequired();

        builder.Property(tenant => tenant.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(tenant => tenant.SubDomain)
            .IsUnique();

        builder.HasData(new
        {
            Id = PlatformTenant.Id,
            Name = PlatformTenant.Name,
            SubDomain = PlatformTenant.SubDomain,
            Kind = TenantKind.Platform,
            DefaultTimeZoneId = "Etc/UTC",
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
