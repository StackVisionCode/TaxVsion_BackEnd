using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentApp.Domain.ProviderCustomers;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Configurations;

public sealed class TenantProviderCustomerConfiguration : IEntityTypeConfiguration<TenantProviderCustomer>
{
    public void Configure(EntityTypeBuilder<TenantProviderCustomer> builder)
    {
        builder.ToTable("TenantProviderCustomers");
        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.TenantId).IsRequired();
        builder.Property(customer => customer.ProviderCode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(customer => customer.Email).HasMaxLength(320).IsRequired();

        builder.OwnsOne(
            customer => customer.CustomerReference,
            reference =>
            {
                reference
                    .Property(r => r.Provider)
                    .HasColumnName("CustomerReferenceProvider")
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();
                reference.Property(r => r.Value).HasColumnName("CustomerReferenceValue").HasMaxLength(200).IsRequired();
            }
        );

        builder
            .HasIndex(customer => new { customer.TenantId, customer.ProviderCode })
            .IsUnique()
            .HasDatabaseName("UX_TenantProviderCustomers_TenantId_ProviderCode");

        builder
            .HasMany(customer => customer.SavedMethods)
            .WithOne()
            .HasForeignKey(method => method.TenantProviderCustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(customer => customer.SavedMethods).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
