using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Domain.Subscriptions.Subscription>
{
    public void Configure(EntityTypeBuilder<Domain.Subscriptions.Subscription> builder)
    {
        builder.HasKey(s => s.Id);

        // Un tenant → una suscripción. El índice único lo garantiza a nivel DB.
        builder.HasIndex(s => s.TenantId).IsUnique();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(s => s.BillingPeriod)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.PlanCode).HasMaxLength(50);
        builder.Property(s => s.PlanName).HasMaxLength(100);

        // ─── Plan change pendiente ─────────────────────────────────────────
        builder.Property(s => s.PendingPlanCode).HasMaxLength(50);
        builder.Property(s => s.PendingPlanName).HasMaxLength(100);

        // ─── Precio período actual (owned types) ──────────────────────────
        builder.OwnsOne(s => s.CurrentBasePrice, m =>
        {
            m.Property(x => x.Amount)
                .HasColumnName("BasePrice_Amount")
                .HasPrecision(19, 4);
            m.Property(x => x.Currency)
                .HasColumnName("BasePrice_Currency")
                .HasMaxLength(3);
        });

        builder.OwnsOne(s => s.CurrentPricePerSeat, m =>
        {
            m.Property(x => x.Amount)
                .HasColumnName("SeatPrice_Amount")
                .HasPrecision(19, 4);
            m.Property(x => x.Currency)
                .HasColumnName("SeatPrice_Currency")
                .HasMaxLength(3);
        });

        // ─── Relación con seats (backing field) ───────────────────────────
        builder.HasMany(s => s.Seats)
            .WithOne()
            .HasForeignKey(s => s.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SeatSubscriptionConfiguration : IEntityTypeConfiguration<SeatSubscription>
{
    public void Configure(EntityTypeBuilder<SeatSubscription> builder)
    {
        builder.HasKey(s => s.Id);

        // Hot-path: buscar seats activos/vencidos por tenant
        builder.HasIndex(s => new { s.TenantId, s.Status });
        builder.HasIndex(s => s.SubscriptionId);
        // Para queries de renovación / monitoreo
        builder.HasIndex(s => s.PeriodEndUtc);

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.BillingPeriod)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.OwnsOne(s => s.PricePerSeat, m =>
        {
            m.Property(x => x.Amount)
                .HasColumnName("PricePerSeat_Amount")
                .HasPrecision(19, 4);
            m.Property(x => x.Currency)
                .HasColumnName("PricePerSeat_Currency")
                .HasMaxLength(3);
        });

        builder.OwnsOne(s => s.TotalAmount, m =>
        {
            m.Property(x => x.Amount)
                .HasColumnName("TotalAmount_Amount")
                .HasPrecision(19, 4);
            m.Property(x => x.Currency)
                .HasColumnName("TotalAmount_Currency")
                .HasMaxLength(3);
        });
    }
}
