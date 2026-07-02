using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Infrastructure.Imports;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

internal sealed class CustomerImportFileConfiguration : IEntityTypeConfiguration<CustomerImportFile>
{
    public void Configure(EntityTypeBuilder<CustomerImportFile> b)
    {
        b.ToTable("CustomerImportFiles");
        b.HasKey(f => f.ImportAttemptId);
        b.Property(f => f.Content).IsRequired();
        b.Property(f => f.UploadedAtUtc).IsRequired();
    }
}
