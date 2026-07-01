using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Subscriptions;
using DomainSubscription = TaxVision.Subscription.Domain.Subscriptions.Subscription;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionModuleConfiguration : IEntityTypeConfiguration<SubscriptionModule>
{
    public void Configure(EntityTypeBuilder<SubscriptionModule> builder)
    {
        builder.HasKey(sm => sm.Id);
        builder.HasIndex(sm => new { sm.SubscriptionId, sm.ModuleId }).IsUnique();
        builder.HasIndex(sm => sm.SubscriptionId);
        builder.HasIndex(sm => sm.ModuleId);
        builder.ToTable("SubscriptionModules");

        builder.HasOne<DomainSubscription>()
            .WithMany(s => s.SubscriptionModules)
            .HasForeignKey(sm => sm.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
