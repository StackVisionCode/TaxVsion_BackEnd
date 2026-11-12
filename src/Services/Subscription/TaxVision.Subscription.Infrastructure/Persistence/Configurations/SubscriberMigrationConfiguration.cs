using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Migrations;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriberMigrationConfiguration : IEntityTypeConfiguration<SubscriberMigration>
{
    public void Configure(EntityTypeBuilder<SubscriberMigration> builder)
    {
        builder.ToTable("SubscriberMigrations");
        builder.HasKey(migration => migration.Id);
        builder.Ignore(migration => migration.DomainEvents);
        builder.Property(migration => migration.PlanCode).HasMaxLength(50).IsRequired();
        builder.Property(migration => migration.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(migration => migration.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(migration => migration.Reason).HasMaxLength(SubscriberMigration.MaxReasonLength).IsRequired();
        builder.Property(migration => migration.CancelReason).HasMaxLength(SubscriberMigration.MaxReasonLength);
        builder.Property(migration => migration.RowVersion).IsRowVersion();
        builder.HasIndex(migration => new { migration.Status, migration.CreatedAtUtc });

        builder
            .HasMany(migration => migration.Offices)
            .WithOne()
            .HasForeignKey(office => office.MigrationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(migration => migration.Offices).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SubscriberMigrationOfficeConfiguration : IEntityTypeConfiguration<SubscriberMigrationOffice>
{
    public void Configure(EntityTypeBuilder<SubscriberMigrationOffice> builder)
    {
        builder.ToTable("SubscriberMigrationOffices");
        builder.HasKey(office => office.Id);
        builder.Property(office => office.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(office => office.Detail).HasMaxLength(200);
        builder.HasIndex(office => new { office.MigrationId, office.TenantId }).IsUnique();
    }
}
