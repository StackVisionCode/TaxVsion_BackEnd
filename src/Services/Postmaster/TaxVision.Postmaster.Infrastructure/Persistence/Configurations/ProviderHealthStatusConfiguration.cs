using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class ProviderHealthStatusConfiguration : IEntityTypeConfiguration<ProviderHealthStatus>
{
    public void Configure(EntityTypeBuilder<ProviderHealthStatus> builder)
    {
        builder.ToTable("ProviderHealthStatuses");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.ProviderKind).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(h => h.TenantId);
        builder.Property(h => h.ProviderCode).HasMaxLength(50).IsRequired();
        builder.Property(h => h.ConsecutiveFailures).IsRequired();
        builder.Property(h => h.ConsecutiveSuccesses).IsRequired();
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        builder.Property(h => h.CircuitBreakerState).HasConversion<string>().HasMaxLength(15).IsRequired();
        builder.Property(h => h.CircuitBreakerOpenedAtUtc);
        builder.Property(h => h.LastCheckAtUtc).IsRequired();
        builder.Property(h => h.LastSuccessAtUtc);
        builder.Property(h => h.LastFailureAtUtc);

        builder
            .HasIndex(h => new
            {
                h.ProviderKind,
                h.TenantId,
                h.ProviderCode,
            })
            .IsUnique();
    }
}
