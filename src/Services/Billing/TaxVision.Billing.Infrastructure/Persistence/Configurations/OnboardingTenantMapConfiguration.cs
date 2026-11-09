using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

public sealed class OnboardingTenantMapConfiguration : IEntityTypeConfiguration<OnboardingTenantMap>
{
    public void Configure(EntityTypeBuilder<OnboardingTenantMap> b)
    {
        b.ToTable("OnboardingTenantMaps");
        b.HasKey(m => m.OnboardingId);
        b.Property(m => m.OnboardingId).ValueGeneratedNever();
        b.Property(m => m.TenantId).IsRequired();
        b.Property(m => m.CreatedAtUtc).IsRequired();
    }
}
