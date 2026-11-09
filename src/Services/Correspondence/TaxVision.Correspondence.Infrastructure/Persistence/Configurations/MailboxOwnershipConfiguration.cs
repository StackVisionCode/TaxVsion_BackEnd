using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class MailboxOwnershipConfiguration : IEntityTypeConfiguration<MailboxOwnership>
{
    public void Configure(EntityTypeBuilder<MailboxOwnership> builder)
    {
        builder.ToTable("MailboxOwnerships");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.OwnerUserId);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.LastChangeAtUtc).IsRequired();

        builder.Ignore(x => x.IsOffice);

        builder
            .HasIndex(x => new { x.TenantId, x.AccountId })
            .IsUnique()
            .HasDatabaseName("IX_MailboxOwnerships_TenantId_AccountId");

        builder
            .HasIndex(x => new { x.TenantId, x.OwnerUserId })
            .HasDatabaseName("IX_MailboxOwnerships_TenantId_OwnerUserId");
    }
}
