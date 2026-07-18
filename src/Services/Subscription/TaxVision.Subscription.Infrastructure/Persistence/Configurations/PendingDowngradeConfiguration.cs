using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class PendingDowngradeConfiguration : IEntityTypeConfiguration<PendingDowngrade>
{
    public void Configure(EntityTypeBuilder<PendingDowngrade> builder)
    {
        builder.ToTable("PendingDowngrades");
        builder.HasKey(pending => pending.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // TenantSubscription._pendingDowngrades (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(pending => pending.Id).ValueGeneratedNever();

        builder.Property(pending => pending.TenantSubscriptionId).IsRequired();
        builder.Property(pending => pending.TenantId).IsRequired();
        builder.Property(pending => pending.FromPlanId).IsRequired();
        builder.Property(pending => pending.FromPlanVersionId).IsRequired();
        builder.Property(pending => pending.FromPlanCode).HasMaxLength(50).IsRequired();
        builder.Property(pending => pending.ToPlanId).IsRequired();
        builder.Property(pending => pending.ToPlanVersionId).IsRequired();
        builder.Property(pending => pending.ToPlanCode).HasMaxLength(50).IsRequired();
        builder.Property(pending => pending.ToBillingCycle).HasConversion<string>().HasMaxLength(20);
        builder.Property(pending => pending.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(pending => pending.RequestedByUserId).IsRequired();
        builder.Property(pending => pending.RequestedAtUtc).IsRequired();
        builder.Property(pending => pending.EffectiveAtUtc).IsRequired();

        builder
            .HasIndex(pending => new { pending.TenantSubscriptionId, pending.Status })
            .HasDatabaseName("IX_PendingDowngrades_TenantSubscriptionId_Status");

        builder
            .HasIndex(pending => pending.EffectiveAtUtc)
            .HasFilter("[Status] = 'Scheduled'")
            .HasDatabaseName("IX_PendingDowngrades_EffectiveAtUtc_Scheduled");
    }
}
