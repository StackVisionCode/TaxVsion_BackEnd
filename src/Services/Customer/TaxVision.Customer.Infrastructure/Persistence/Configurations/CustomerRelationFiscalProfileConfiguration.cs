using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerRelationFiscalProfileConfiguration : IEntityTypeConfiguration<CustomerRelationFiscalProfile>
{
    public void Configure(EntityTypeBuilder<CustomerRelationFiscalProfile> b)
    {
        b.ToTable("CustomerRelationFiscalProfiles");
        b.HasKey(fp => fp.Id);
        b.Property(fp => fp.TenantId).IsRequired();
        b.Property(fp => fp.CustomerRelationId).IsRequired();
        b.Property(fp => fp.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(fp => fp.TaxIdentifierCipher).HasColumnType("varbinary(512)").IsRequired();
        b.Property(fp => fp.TaxIdentifierBlindIndex).HasMaxLength(64).IsRequired();
        b.Property(fp => fp.TaxIdentifierLast4).HasMaxLength(4).IsRequired();
        b.Property(fp => fp.TaxYear).IsRequired();
        b.Property(fp => fp.QualifiesAsDependent).IsRequired();
        b.Property(fp => fp.LivedWithTaxpayer).IsRequired();
        b.Property(fp => fp.UpdatedAtUtc).IsRequired();
        b.Property(fp => fp.UpdatedByUserId).IsRequired();

        b.HasIndex(fp => fp.CustomerRelationId).IsUnique();
        b.HasIndex(fp => new { fp.TenantId, fp.TaxIdentifierBlindIndex });
    }
}
