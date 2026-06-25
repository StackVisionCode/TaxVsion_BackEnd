using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

        builder.Property(tenant => tenant.IsActive)
            .IsRequired();

        builder.Property(tenant => tenant.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(tenant => tenant.SubDomain)
            .IsUnique();

        builder.Ignore(tenant => tenant.DomainEvents);
    }
}
