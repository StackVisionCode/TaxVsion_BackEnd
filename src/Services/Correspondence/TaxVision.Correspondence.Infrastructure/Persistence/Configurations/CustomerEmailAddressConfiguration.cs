using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class CustomerEmailAddressConfiguration : IEntityTypeConfiguration<CustomerEmailAddress>
{
    public void Configure(EntityTypeBuilder<CustomerEmailAddress> builder)
    {
        builder.ToTable("CustomerEmailAddresses");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.EmailAddress).IsRequired().HasMaxLength(320);
        builder.Property(x => x.IsPrimary).IsRequired();
        builder.Property(x => x.Source).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.DeletedAtUtc);

        builder.Ignore(x => x.IsActive);

        // Un email solo puede pertenecer a un customer activo por tenant a la vez;
        // filtrado por DeletedAtUtc para permitir que un email libere su lugar tras
        // el soft-delete (p. ej. si el customer se archiva y otro toma el mismo email).
        builder
            .HasIndex(x => new { x.TenantId, x.EmailAddress })
            .IsUnique()
            .HasFilter("[DeletedAtUtc] IS NULL")
            .HasDatabaseName("IX_CustomerEmailAddresses_TenantId_EmailAddress_Active");

        builder
            .HasIndex(x => new { x.TenantId, x.CustomerId })
            .HasDatabaseName("IX_CustomerEmailAddresses_TenantId_CustomerId");
    }
}
