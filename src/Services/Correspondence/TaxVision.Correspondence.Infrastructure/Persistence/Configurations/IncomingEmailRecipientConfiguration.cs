using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class IncomingEmailRecipientConfiguration : IEntityTypeConfiguration<IncomingEmailRecipient>
{
    public void Configure(EntityTypeBuilder<IncomingEmailRecipient> builder)
    {
        builder.ToTable("IncomingEmailRecipients");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IncomingEmailId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Address).IsRequired().HasMaxLength(EmailAddress.MaxLength);
        builder.Property(x => x.Type).IsRequired().HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.DisplayName).HasMaxLength(IncomingEmailRecipient.DisplayNameMaxLength);

        builder
            .HasIndex(x => new { x.TenantId, x.Address })
            .HasDatabaseName("IX_IncomingEmailRecipients_TenantId_Address");
    }
}
