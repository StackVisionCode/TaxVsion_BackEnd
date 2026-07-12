using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<DomainCustomer>
{
    public void Configure(EntityTypeBuilder<DomainCustomer> b)
    {
        b.ToTable("Customers");
        b.HasKey(c => c.Id);
        b.Property(c => c.TenantId).IsRequired();
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(c => c.DateOfBirth);
        b.Property(c => c.PreferredChannel).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(c => c.Language).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(c => c.ProfilePictureFileId);
        b.Property(c => c.OccupationId);
        b.Property(c => c.CreatedAtUtc).IsRequired();
        b.Property(c => c.CreatedByUserId).IsRequired();
        b.Property(c => c.UpdatedAtUtc);
        b.Property(c => c.LastModifiedByUserId);
        b.Property(c => c.ArchivedAtUtc);
        b.Property(c => c.ArchivedByUserId);

        // ---- Owned types: VOs embebidos como columnas dentro de Customers ----

        b.OwnsOne(
            c => c.PersonalName,
            n =>
            {
                n.Property(p => p.Prefix).HasColumnName("PersonalName_Prefix").HasMaxLength(20);
                n.Property(p => p.FirstName).HasColumnName("PersonalName_FirstName").HasMaxLength(80);
                n.Property(p => p.MiddleName).HasColumnName("PersonalName_MiddleName").HasMaxLength(80);
                n.Property(p => p.LastName).HasColumnName("PersonalName_LastName").HasMaxLength(80);
                n.Property(p => p.Suffix).HasColumnName("PersonalName_Suffix").HasMaxLength(20);
                n.Ignore(p => p.DisplayName);
            }
        );

        b.OwnsOne(
            c => c.BusinessIdentity,
            biz =>
            {
                biz.Property(p => p.LegalName).HasColumnName("Business_LegalName").HasMaxLength(200);
                biz.Property(p => p.Dba).HasColumnName("Business_Dba").HasMaxLength(200);
                biz.Property(p => p.Structure)
                    .HasColumnName("Business_Structure")
                    .HasConversion<string>()
                    .HasMaxLength(30);
                biz.Property(p => p.FormationDate).HasColumnName("Business_FormationDate");
                biz.Property(p => p.PrincipalBusinessActivityId).HasColumnName("Business_PrincipalBusinessActivityId");
            }
        );

        b.OwnsOne(
            c => c.PrimaryEmail,
            e =>
            {
                e.Property(p => p.Value).HasColumnName("PrimaryEmail").HasMaxLength(254).IsRequired();
                e.Property(p => p.NormalizedValue)
                    .HasColumnName("PrimaryEmailNormalized")
                    .HasMaxLength(254)
                    .IsRequired();
                e.HasIndex(p => p.NormalizedValue).HasDatabaseName("IX_Customers_PrimaryEmailNormalized");
            }
        );

        b.OwnsOne(
            c => c.PrimaryPhone,
            p =>
            {
                p.Property(x => x.E164Value).HasColumnName("PrimaryPhone").HasMaxLength(20);
            }
        );

        // ---- Child entities navigation ----

        b.HasMany(c => c.Addresses).WithOne().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.NoAction);
        b.Navigation(c => c.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(c => c.ContactPoints).WithOne().HasForeignKey(cp => cp.CustomerId).OnDelete(DeleteBehavior.NoAction);
        b.Navigation(c => c.ContactPoints).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(c => c.Relations).WithOne().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.NoAction);
        b.Navigation(c => c.Relations).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasOne(c => c.FiscalProfile)
            .WithOne()
            .HasForeignKey<TaxVision.Customer.Domain.FiscalProfiles.CustomerFiscalProfile>(fp => fp.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        // ---- Indices del PDF (sec. 5 - Paso 5) ----

        b.HasIndex(c => new
        {
            c.TenantId,
            c.Status,
            c.DisplayName,
        });
        b.HasIndex(c => new { c.TenantId, c.OccupationId });
    }
}
