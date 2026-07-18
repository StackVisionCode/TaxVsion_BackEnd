using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Catalogs;
using TaxVision.Customer.Infrastructure.Persistence.Seeds;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class OccupationConfiguration : IEntityTypeConfiguration<Occupation>
{
    public void Configure(EntityTypeBuilder<Occupation> b)
    {
        b.ToTable("Occupations");
        b.HasKey(o => o.Id);
        b.Property(o => o.Name).HasMaxLength(120).IsRequired();
        b.Property(o => o.DisplayOrder).IsRequired();
        b.Property(o => o.IsActive).IsRequired();
        b.HasIndex(o => o.Name);

        b.HasData(OccupationSeed.All);
    }
}
