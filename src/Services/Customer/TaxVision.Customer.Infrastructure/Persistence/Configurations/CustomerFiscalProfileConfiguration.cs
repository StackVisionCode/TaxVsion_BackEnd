using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerFiscalProfileConfiguration : IEntityTypeConfiguration<CustomerFiscalProfile>
{
    public void Configure(EntityTypeBuilder<CustomerFiscalProfile> b)
    {
        b.ToTable("CustomerFiscalProfiles");
        b.HasKey(fp => fp.Id);
        b.Property(fp => fp.TenantId).IsRequired();
        b.Property(fp => fp.CustomerId).IsRequired();
        b.Property(fp => fp.SubjectKind).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(fp => fp.TaxIdentifierCipher).HasColumnType("varbinary(512)").IsRequired();
        b.Property(fp => fp.TaxIdentifierBlindIndex).HasMaxLength(64).IsRequired();
        b.Property(fp => fp.TaxIdentifierLast4).HasMaxLength(4).IsRequired();
        b.Property(fp => fp.FilingStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(fp => fp.PriorYearAgi).HasColumnType("decimal(18,2)");
        b.Property(fp => fp.IsReturningCustomer).IsRequired();
        b.Property(fp => fp.RefundBankAccountCipher).HasColumnType("varbinary(512)");
        b.Property(fp => fp.RefundBankRoutingCipher).HasColumnType("varbinary(512)");
        b.Property(fp => fp.UpdatedAtUtc).IsRequired();
        b.Property(fp => fp.UpdatedByUserId).IsRequired();

        b.HasIndex(fp => fp.CustomerId).IsUnique();
        b.HasIndex(fp => new { fp.TenantId, fp.TaxIdentifierBlindIndex });
    }
}
