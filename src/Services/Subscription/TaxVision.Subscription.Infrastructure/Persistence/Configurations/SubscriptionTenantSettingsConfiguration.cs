using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionTenantSettingsConfiguration : IEntityTypeConfiguration<SubscriptionTenantSettings>
{
    public void Configure(EntityTypeBuilder<SubscriptionTenantSettings> builder)
    {
        builder.ToTable("SubscriptionTenantSettings");
        builder.HasKey(settings => settings.Id);
        builder.HasIndex(settings => settings.TenantId).IsUnique();

        builder.Property(settings => settings.AllowAutoRenewTenantSubscription).IsRequired();
        builder.Property(settings => settings.AllowAutoRenewSeats).IsRequired();
        builder.Property(settings => settings.AllowSeatSelfAssignment).IsRequired();
        builder.Property(settings => settings.AllowAdminSeatAssignment).IsRequired();
        builder.Property(settings => settings.MaxSeatsAllowed);
        builder.Property(settings => settings.MinSeatsRequired).IsRequired();
        builder.Property(settings => settings.DefaultSeatRenewalDays).IsRequired();
        builder.Property(settings => settings.AllowSeatReassignment).IsRequired();
        builder.Property(settings => settings.SeatReassignmentCooldownDays).IsRequired();
        builder.Property(settings => settings.AllowAddons).IsRequired();
        builder.Property(settings => settings.AllowTrial).IsRequired();
        builder.Property(settings => settings.SuspendTenantWhenBaseSubscriptionExpired).IsRequired();
        builder.Property(settings => settings.SuspendUserWhenSeatExpired).IsRequired();
        builder.Property(settings => settings.NotifyAfterFailedRenewalDays).IsRequired();
        builder
            .Property(settings => settings.AutoRenewCascadeMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(settings => settings.PauseSeatRenewalsWhenBaseSuspended).IsRequired();
        builder
            .Property(settings => settings.PlanChangeEffective)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder
            .Property(settings => settings.TenantSubscriptionGracePeriod)
            .HasConversion(grace => grace.Days, days => GracePeriod.Create(days).Value)
            .HasColumnName("TenantSubscriptionGraceDays")
            .IsRequired();

        builder
            .Property(settings => settings.SeatGracePeriod)
            .HasConversion(grace => grace.Days, days => GracePeriod.Create(days).Value)
            .HasColumnName("SeatGraceDays")
            .IsRequired();

        builder
            .Property(settings => settings.TrialDays)
            .HasConversion(trial => trial.Value, days => TrialDays.Create(days).Value)
            .HasColumnName("TrialDays")
            .IsRequired();

        var notifyDaysConverter = new ValueConverter<List<int>, string>(
            days => string.Join(',', days),
            csv => ParseDays(csv)
        );
        var notifyDaysComparer = new ValueComparer<List<int>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            list => list.Aggregate(0, (hash, day) => HashCode.Combine(hash, day)),
            list => list.ToList()
        );

        builder
            .Property<List<int>>("_notifyBeforeRenewalDays")
            .HasColumnName("NotifyBeforeRenewalDays")
            .HasConversion(notifyDaysConverter)
            .HasMaxLength(100)
            .Metadata.SetValueComparer(notifyDaysComparer);
    }

    private static List<int> ParseDays(string csv) =>
        string.IsNullOrEmpty(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
}
