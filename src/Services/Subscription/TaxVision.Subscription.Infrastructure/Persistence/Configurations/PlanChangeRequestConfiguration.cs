using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class PlanChangeRequestConfiguration : IEntityTypeConfiguration<PlanChangeRequest>
{
    public void Configure(EntityTypeBuilder<PlanChangeRequest> builder)
    {
        builder.ToTable("PlanChangeRequests");
        builder.HasKey(request => request.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // TenantSubscription._planChangeRequests (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(request => request.Id).ValueGeneratedNever();

        builder.Property(request => request.TenantSubscriptionId).IsRequired();
        builder.Property(request => request.TenantId).IsRequired();
        builder.Property(request => request.FromPlanId).IsRequired();
        builder.Property(request => request.FromPlanVersionId).IsRequired();
        builder.Property(request => request.FromPlanCode).HasMaxLength(50).IsRequired();
        builder.Property(request => request.ToPlanId).IsRequired();
        builder.Property(request => request.ToPlanVersionId).IsRequired();
        builder.Property(request => request.ToPlanCode).HasMaxLength(50).IsRequired();
        builder.Property(request => request.EffectiveMode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(request => request.RequestedByUserId).IsRequired();
        builder.Property(request => request.RequestedAtUtc).IsRequired();
        builder.Property(request => request.EffectiveAtUtc).IsRequired();

        builder
            .HasIndex(request => new { request.TenantSubscriptionId, request.Status })
            .HasDatabaseName("IX_PlanChangeRequests_TenantSubscriptionId_Status");

        builder
            .HasIndex(request => request.EffectiveAtUtc)
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("IX_PlanChangeRequests_EffectiveAtUtc_Pending");
    }
}
