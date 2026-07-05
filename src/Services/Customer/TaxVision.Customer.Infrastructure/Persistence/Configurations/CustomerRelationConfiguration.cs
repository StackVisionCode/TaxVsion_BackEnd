using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerRelationConfiguration : IEntityTypeConfiguration<CustomerRelation>
{
    public void Configure(EntityTypeBuilder<CustomerRelation> b)
    {
        b.ToTable("CustomerRelations");
        b.HasKey(r => r.Id);
        b.Property(r => r.TenantId).IsRequired();
        b.Property(r => r.CustomerId).IsRequired();
        b.Property(r => r.RelationshipKind).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(r => r.Purposes).HasConversion<int>().IsRequired(); // Flags enum -> bitmask int
        b.Property(r => r.DateOfBirth);
        b.Property(r => r.IsActive).IsRequired();

        b.OwnsOne(
            r => r.Name,
            n =>
            {
                n.Property(p => p.Prefix).HasColumnName("Name_Prefix").HasMaxLength(20);
                n.Property(p => p.FirstName).HasColumnName("Name_FirstName").HasMaxLength(80).IsRequired();
                n.Property(p => p.MiddleName).HasColumnName("Name_MiddleName").HasMaxLength(80);
                n.Property(p => p.LastName).HasColumnName("Name_LastName").HasMaxLength(80).IsRequired();
                n.Property(p => p.Suffix).HasColumnName("Name_Suffix").HasMaxLength(20);
                n.Ignore(p => p.DisplayName);
            }
        );

        b.OwnsOne(
            r => r.PrimaryEmail,
            e =>
            {
                e.Property(p => p.Value).HasColumnName("PrimaryEmail").HasMaxLength(254);
                e.Property(p => p.NormalizedValue).HasColumnName("PrimaryEmailNormalized").HasMaxLength(254);
            }
        );

        b.OwnsOne(
            r => r.PrimaryPhone,
            p =>
            {
                p.Property(x => x.E164Value).HasColumnName("PrimaryPhone").HasMaxLength(20);
            }
        );

        // AddressValue inline (no su propia tabla) — match con la guía sec. 4.2
        b.OwnsOne(
            r => r.Address,
            addr =>
            {
                addr.Property(p => p.Line1).HasColumnName("Address_Line1").HasMaxLength(200);
                addr.Property(p => p.Line2).HasColumnName("Address_Line2").HasMaxLength(200);
                addr.Property(p => p.City).HasColumnName("Address_City").HasMaxLength(100);
                addr.Property(p => p.Region).HasColumnName("Address_Region").HasMaxLength(100);
                addr.Property(p => p.PostalCode).HasColumnName("Address_PostalCode").HasMaxLength(20);
                addr.Property(p => p.CountryCode).HasColumnName("Address_CountryCode").HasMaxLength(2);
            }
        );

        b.HasOne(r => r.FiscalProfile)
            .WithOne()
            .HasForeignKey<CustomerRelationFiscalProfile>(fp => fp.CustomerRelationId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(r => new
        {
            r.TenantId,
            r.CustomerId,
            r.RelationshipKind,
        });
    }
}
