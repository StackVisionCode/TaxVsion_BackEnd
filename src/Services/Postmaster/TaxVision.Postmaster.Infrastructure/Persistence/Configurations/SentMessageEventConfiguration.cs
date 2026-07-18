using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class SentMessageEventConfiguration : IEntityTypeConfiguration<SentMessageEvent>
{
    public void Configure(EntityTypeBuilder<SentMessageEvent> builder)
    {
        builder.ToTable("SentMessageEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.SentMessageId).IsRequired();
        builder.Property(e => e.RecipientId);
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(15).IsRequired();
        builder.Property(e => e.EventAtUtc).IsRequired();
        builder.Property(e => e.RawPayload).HasMaxLength(8192);
        builder.Property(e => e.Reason).HasMaxLength(500);

        builder.HasIndex(e => new { e.SentMessageId, e.EventAtUtc });
    }
}
