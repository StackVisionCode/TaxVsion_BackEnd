using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionSeatConfiguration : IEntityTypeConfiguration<SubscriptionSeat>
{
    public void Configure(EntityTypeBuilder<SubscriptionSeat> builder)
    {
        builder.ToTable("SubscriptionSeats");
        builder.HasKey(seat => seat.Id);

        builder.Property(seat => seat.TenantId).IsRequired();
        builder.Property(seat => seat.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(seat => seat.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(seat => seat.SourceType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(seat => seat.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(seat => seat.CancellationReason).HasMaxLength(500);
        builder.Property(seat => seat.SuspensionReason).HasMaxLength(500);

        builder.OwnsOne(seat => seat.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("UnitPriceAmount").HasPrecision(18, 4).IsRequired();
            money.Property(m => m.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(seat => new { seat.TenantId, seat.Status });

        builder.HasIndex(seat => seat.CurrentUserId)
            .HasFilter("[CurrentUserId] IS NOT NULL")
            .HasDatabaseName("IX_SubscriptionSeats_CurrentUserId");

        builder.HasIndex(seat => seat.NextRenewalAtUtc)
            .HasFilter("[Status] IN ('Active','PastDue') AND [AutoRenew] = 1")
            .HasDatabaseName("IX_SubscriptionSeats_NextRenewalAtUtc");

        builder.HasMany(seat => seat.Assignments)
            .WithOne()
            .HasForeignKey(assignment => assignment.SeatId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(seat => seat.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(seat => seat.Renewals)
            .WithOne()
            .HasForeignKey(renewal => renewal.SeatId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(seat => seat.Renewals).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
