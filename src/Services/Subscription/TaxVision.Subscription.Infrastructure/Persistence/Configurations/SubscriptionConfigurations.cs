using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Code).HasMaxLength(32).IsRequired();
        builder.Property(plan => plan.Name).HasMaxLength(64).IsRequired();
        builder.Property(plan => plan.Description).HasMaxLength(512).IsRequired();
        builder.Property(plan => plan.MonthlyPriceUsd).HasPrecision(10, 2);
        builder.Property(plan => plan.EnabledModulesJson).HasMaxLength(2048).IsRequired();
        builder.HasIndex(plan => plan.Code).IsUnique();

        builder.HasData(
            new
            {
                Id = PlanCatalog.StarterId,
                Code = PlanCatalog.Starter,
                Name = "Starter",
                Description = "Para oficinas que están empezando: 3 usuarios, clientes, firmas y documentos.",
                MonthlyPriceUsd = 49m,
                MaxUsers = 3,
                MaxPendingInvitations = 5,
                StorageQuotaBytes = 10L * 1024 * 1024 * 1024,
                EnabledModulesJson = """["customers","signatures","documents","planner"]""",
                IsActive = true,
                SortOrder = 1
            },
            new
            {
                Id = PlanCatalog.ProId,
                Code = PlanCatalog.Pro,
                Name = "Pro",
                Description = "Para oficinas en crecimiento: 10 usuarios, correo, comunicación y campañas.",
                MonthlyPriceUsd = 129m,
                MaxUsers = 10,
                MaxPendingInvitations = 15,
                StorageQuotaBytes = 50L * 1024 * 1024 * 1024,
                EnabledModulesJson =
                    """["customers","signatures","documents","planner","email","comms","campaigns","reports"]""",
                IsActive = true,
                SortOrder = 2
            },
            new
            {
                Id = PlanCatalog.EnterpriseId,
                Code = PlanCatalog.Enterprise,
                Name = "Enterprise",
                Description = "Para multiservices con equipos grandes: 25 usuarios y todos los módulos.",
                MonthlyPriceUsd = 299m,
                MaxUsers = 25,
                MaxPendingInvitations = 40,
                StorageQuotaBytes = 200L * 1024 * 1024 * 1024,
                EnabledModulesJson =
                    """["customers","signatures","documents","planner","email","comms","campaigns","reports","marketing","builder","irs","miles"]""",
                IsActive = true,
                SortOrder = 3
            });
    }
}

public sealed class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.TenantId).IsRequired();
        builder.Property(subscription => subscription.PlanCode).HasMaxLength(32).IsRequired();
        builder.Property(subscription => subscription.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(subscription => subscription.SuspensionReason).HasMaxLength(128);

        builder.HasIndex(subscription => subscription.TenantId).IsUnique();
        builder.HasIndex(subscription => subscription.Status);

        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(subscription => subscription.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
