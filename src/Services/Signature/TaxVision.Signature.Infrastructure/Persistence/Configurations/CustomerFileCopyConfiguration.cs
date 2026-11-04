using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class CustomerFileCopyConfiguration : IEntityTypeConfiguration<CustomerFileCopy>
{
    public void Configure(EntityTypeBuilder<CustomerFileCopy> builder)
    {
        builder.ToTable("SignatureCustomerFileCopies");
        builder.HasKey(copy => copy.Id);
        builder.Property(copy => copy.Id).ValueGeneratedNever();
        builder.Property(copy => copy.SourceFileId).IsRequired();
        builder.Property(copy => copy.CustomerId).IsRequired();
        builder.Property(copy => copy.CopyFileId).IsRequired();
        builder.Property(copy => copy.CreatedAtUtc).IsRequired();

        // Una sola copia por (archivo, cliente): dos entregas concurrentes no duplican.
        builder
            .HasIndex(copy => new
            {
                copy.SignatureRequestId,
                copy.SourceFileId,
                copy.CustomerId,
            })
            .IsUnique();
    }
}
