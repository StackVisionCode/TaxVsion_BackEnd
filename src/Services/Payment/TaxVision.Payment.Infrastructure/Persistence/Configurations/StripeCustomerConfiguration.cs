using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Payment.Domain.StripeCustomers;

namespace TaxVision.Payment.Infrastructure.Persistence.Configurations;

public sealed class StripeCustomerConfiguration : IEntityTypeConfiguration<StripeCustomer>
{
    public void Configure(EntityTypeBuilder<StripeCustomer> builder)
    {
        builder.ToTable("StripeCustomers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.StripeCustomerId).HasMaxLength(255).IsRequired();
        builder.Property(c => c.AdminEmail).HasMaxLength(320).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.HasIndex(c => c.TenantId).IsUnique();
        builder.HasIndex(c => c.StripeCustomerId).IsUnique();
    }
}
