using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

public sealed class ProviderWatchSubscriptionConfiguration : IEntityTypeConfiguration<ProviderWatchSubscription>
{
    public void Configure(EntityTypeBuilder<ProviderWatchSubscription> builder)
    {
        builder.ToTable("ProviderWatchSubscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.AccountId).IsRequired();
        builder.Property(s => s.ProviderCode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.SubscriptionRef).HasMaxLength(500).IsRequired();
        builder.Property(s => s.TopicName).HasMaxLength(500);
        builder.Property(s => s.ExpiresAtUtc).IsRequired();
        builder.Property(s => s.LastRenewedAtUtc).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.FailureCount).IsRequired();

        builder.HasIndex(s => s.AccountId).IsUnique();
        builder.HasIndex(s => s.ExpiresAtUtc);
    }
}
