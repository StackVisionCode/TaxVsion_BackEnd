using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerImportRowConfiguration : IEntityTypeConfiguration<CustomerImportRow>
{
    public void Configure(EntityTypeBuilder<CustomerImportRow> b)
    {
        b.ToTable("CustomerImportRows");
        b.HasKey(r => r.Id);
        b.Property(r => r.TenantId).IsRequired();
        b.Property(r => r.CustomerImportAttemptId).IsRequired();
        b.Property(r => r.RowNumber).IsRequired();
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(r => r.ResultingCustomerId);
        b.Property(r => r.DisplayName).HasMaxLength(200);
        b.Property(r => r.MatchedBy).HasMaxLength(200);
        b.Property(r => r.ErrorCode).HasMaxLength(80);
        b.Property(r => r.Message).HasMaxLength(500);

        b.HasIndex(r => new { r.CustomerImportAttemptId, r.RowNumber })
            .HasDatabaseName("IX_CustomerImportRows_Attempt_Row");
        b.HasIndex(r => new { r.CustomerImportAttemptId, r.Status })
            .HasDatabaseName("IX_CustomerImportRows_Attempt_Status");
    }
}
