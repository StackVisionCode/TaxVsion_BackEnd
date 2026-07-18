using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Idempotency;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class EmailIdempotencyConfiguration : IEntityTypeConfiguration<EmailIdempotency>
{
    public void Configure(EntityTypeBuilder<EmailIdempotency> builder)
    {
        builder.ToTable("EmailIdempotency");
        builder.HasKey(e => new { e.TenantId, e.IdempotencyKey });
        builder.Property(e => e.IdempotencyKey).HasMaxLength(200);
        builder.Property(e => e.SentMessageId);
        builder.Property(e => e.CompletedAtUtc);
        builder.Property(e => e.ExpiresAtUtc).IsRequired();
        builder.Property(e => e.CreatedAtUtc).IsRequired();

        builder.HasIndex(e => e.ExpiresAtUtc);
    }
}
