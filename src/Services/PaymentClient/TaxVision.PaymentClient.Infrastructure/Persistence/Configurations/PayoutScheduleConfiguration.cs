using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.Payouts;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class PayoutScheduleConfiguration : IEntityTypeConfiguration<PayoutSchedule>
{
    public void Configure(EntityTypeBuilder<PayoutSchedule> builder)
    {
        builder.ToTable("PayoutSchedules");
        builder.HasKey(schedule => schedule.Id);

        builder.Property(schedule => schedule.TenantId).IsRequired();
        builder.Property(schedule => schedule.TenantConnectAccountId).IsRequired();
        builder.Property(schedule => schedule.Frequency).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(schedule => schedule.Anchor);
        builder.Property(schedule => schedule.Currency).HasMaxLength(3).IsRequired();
        builder.Property(schedule => schedule.CreatedAtUtc).IsRequired();
        builder.Property(schedule => schedule.UpdatedAtUtc).IsRequired();
        builder.Property(schedule => schedule.UpdatedBy).IsRequired();

        builder
            .HasIndex(schedule => schedule.TenantConnectAccountId)
            .IsUnique()
            .HasDatabaseName("UX_PayoutSchedules_TenantConnectAccountId");

        builder
            .HasMany(schedule => schedule.Items)
            .WithOne()
            .HasForeignKey(item => item.PayoutScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(schedule => schedule.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
