using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Infrastructure.Persistence.Configurations;

/// <summary>Perfil público de la oficina: una fila por tenant; direcciones y horario como JSON.</summary>
public sealed class OfficeProfileConfiguration : IEntityTypeConfiguration<OfficeProfile>
{
    public void Configure(EntityTypeBuilder<OfficeProfile> b)
    {
        b.ToTable("OfficeProfiles");
        b.HasKey(p => p.TenantId);
        b.Property(p => p.TenantId).ValueGeneratedNever();

        b.Property(p => p.LegalName).HasMaxLength(OfficeLegalName.MaxLength);
        b.Property(p => p.DisplayName).HasMaxLength(DoingBusinessAsName.MaxLength);
        b.Property(p => p.EntityType).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Ein).HasMaxLength(10);
        b.Property(p => p.Phone).HasMaxLength(20);
        b.Property(p => p.Email).HasMaxLength(ContactEmail.MaxLength);
        b.Property(p => p.Website).HasMaxLength(OfficeProfileDetails.WebsiteMaxLength);
        b.Property(p => p.DefaultLanguage).HasMaxLength(2).IsRequired();
        b.Property(p => p.BookingUrl).HasMaxLength(OfficeProfileDetails.LinkMaxLength);
        b.Property(p => p.ReviewUrl).HasMaxLength(OfficeProfileDetails.LinkMaxLength);

        // Dos guardados simultáneos no pueden publicar la misma versión con estados distintos: el segundo falla.
        b.Property(p => p.Version).IsConcurrencyToken();

        b.OwnsOne(p => p.PhysicalAddress, address => address.ToJson("PhysicalAddressJson"));
        b.OwnsOne(p => p.MailingAddress, address => address.ToJson("MailingAddressJson"));
        b.OwnsMany(
            p => p.BusinessHours,
            hours =>
            {
                hours.ToJson("BusinessHoursJson");
                hours.Property(h => h.Day).HasConversion<string>();
            }
        );
        b.Navigation(p => p.BusinessHours).HasField("_businessHours");
        b.OwnsMany(p => p.Closures, closures => closures.ToJson("ClosuresJson"));
        b.Navigation(p => p.Closures).HasField("_closures");
    }
}
