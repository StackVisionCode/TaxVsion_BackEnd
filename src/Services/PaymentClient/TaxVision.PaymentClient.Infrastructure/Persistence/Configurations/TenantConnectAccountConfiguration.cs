using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.PaymentClient.Domain.Connect;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class TenantConnectAccountConfiguration : IEntityTypeConfiguration<TenantConnectAccount>
{
    public void Configure(EntityTypeBuilder<TenantConnectAccount> builder)
    {
        builder.ToTable("TenantConnectAccounts");
        builder.HasKey(account => account.Id);

        builder.Property(account => account.TenantId).IsRequired();
        builder.Property(account => account.ProviderCode).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder
            .Property(account => account.StripeConnectAccountId)
            .HasConversion(id => id.Value, value => StripeConnectAccountId.Create(value).Value)
            .HasColumnName("StripeConnectAccountId")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(account => account.AccountType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(account => account.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(account => account.OnboardingStep).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(account => account.CanCharge).IsRequired();
        builder.Property(account => account.CanReceivePayouts).IsRequired();

        var requirementsConverter = new ValueConverter<IReadOnlyList<string>, string>(
            requirements => string.Join(',', requirements),
            csv => ParseRequirements(csv)
        );
        var requirementsComparer = new ValueComparer<IReadOnlyList<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            list => list.Aggregate(0, (hash, requirement) => HashCode.Combine(hash, requirement)),
            list => list.ToList()
        );

        builder
            .Property(account => account.RequirementsCurrentlyDue)
            .HasColumnName("RequirementsCurrentlyDue")
            .HasConversion(requirementsConverter)
            .HasMaxLength(2000)
            .Metadata.SetValueComparer(requirementsComparer);

        builder.Property(account => account.CreatedAtUtc).IsRequired();
        builder.Property(account => account.UpdatedAtUtc).IsRequired();

        builder
            .HasIndex(account => new { account.TenantId, account.ProviderCode })
            .IsUnique()
            .HasDatabaseName("UX_TenantConnectAccounts_TenantId_ProviderCode");

        builder
            .HasIndex(account => account.StripeConnectAccountId)
            .IsUnique()
            .HasDatabaseName("UX_TenantConnectAccounts_StripeConnectAccountId");
    }

    private static List<string> ParseRequirements(string csv) =>
        string.IsNullOrEmpty(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
}
