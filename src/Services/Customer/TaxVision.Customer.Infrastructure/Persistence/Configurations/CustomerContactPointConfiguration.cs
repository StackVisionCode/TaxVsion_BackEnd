using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.ContactPoints;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerContactPointConfiguration : IEntityTypeConfiguration<CustomerContactPoint>
{
    public void Configure(EntityTypeBuilder<CustomerContactPoint> b)
    {
        b.ToTable("CustomerContactPoints");
        b.HasKey(c => c.Id);
        b.Property(c => c.TenantId).IsRequired();
        b.Property(c => c.CustomerId).IsRequired();
        b.Property(c => c.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(c => c.Value).HasMaxLength(254).IsRequired();
        b.Property(c => c.NormalizedValue).HasMaxLength(254).IsRequired();
        b.Property(c => c.Label).HasMaxLength(40);
        b.Property(c => c.IsPrimary).IsRequired();
        b.Property(c => c.VerifiedAtUtc);

        b.HasIndex(c => new
        {
            c.TenantId,
            c.CustomerId,
            c.Type,
        });
        b.HasIndex(c => new
            {
                c.TenantId,
                c.CustomerId,
                c.Type,
                c.IsPrimary,
            })
            .IsUnique()
            .HasFilter("[IsPrimary] = 1");
        b.HasIndex(c => new
        {
            c.TenantId,
            c.Type,
            c.NormalizedValue,
        });
    }
}
