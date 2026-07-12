using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Addresses;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> b)
    {
        b.ToTable("CustomerAddresses");
        b.HasKey(a => a.Id);
        b.Property(a => a.TenantId).IsRequired();
        b.Property(a => a.CustomerId).IsRequired();
        b.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(a => a.IsPrimary).IsRequired();

        b.OwnsOne(
            a => a.Address,
            addr =>
            {
                addr.Property(p => p.Line1).HasColumnName("Line1").HasMaxLength(200).IsRequired();
                addr.Property(p => p.Line2).HasColumnName("Line2").HasMaxLength(200);
                addr.Property(p => p.City).HasColumnName("City").HasMaxLength(100).IsRequired();
                addr.Property(p => p.Region).HasColumnName("Region").HasMaxLength(100);
                addr.Property(p => p.PostalCode).HasColumnName("PostalCode").HasMaxLength(20).IsRequired();
                addr.Property(p => p.CountryCode).HasColumnName("CountryCode").HasMaxLength(2).IsRequired();
            }
        );

        b.HasIndex(a => new
        {
            a.TenantId,
            a.CustomerId,
            a.Kind,
        });
        b.HasIndex(a => new
            {
                a.TenantId,
                a.CustomerId,
                a.IsPrimary,
            })
            .HasFilter("[IsPrimary] = 1");
    }
}
