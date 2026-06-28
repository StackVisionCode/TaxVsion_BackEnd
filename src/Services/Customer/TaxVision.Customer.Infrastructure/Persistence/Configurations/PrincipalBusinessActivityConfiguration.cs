using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Catalogs;
using TaxVision.Customer.Infrastructure.Persistence.Seeds;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class PrincipalBusinessActivityConfiguration : IEntityTypeConfiguration<PrincipalBusinessActivity>
{
    public void Configure(EntityTypeBuilder<PrincipalBusinessActivity> b)
    {
        b.ToTable("PrincipalBusinessActivities");
        b.HasKey(p => p.Id);
        b.Property(p => p.NaicsCode).HasMaxLength(6).IsRequired();
        b.Property(p => p.Description).HasMaxLength(300).IsRequired();
        b.Property(p => p.Sector).HasMaxLength(80);
        b.Property(p => p.IsActive).IsRequired();
        b.HasIndex(p => p.NaicsCode).IsUnique();
        b.HasIndex(p => p.Sector);

        b.HasData(PrincipalBusinessActivitySeed.All);
    }
}
