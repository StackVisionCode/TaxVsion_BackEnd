using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignatureAuditEventConfiguration : IEntityTypeConfiguration<SignatureAuditEvent>
{
    public void Configure(EntityTypeBuilder<SignatureAuditEvent> builder)
    {
        builder.ToTable("SignatureAuditEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.SignatureRequestId).IsRequired();
        builder.Property(e => e.Sequence).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.OccurredAtUtc).IsRequired();
        builder.Property(e => e.PayloadJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(e => e.PreviousChainHash).IsRequired().HasMaxLength(SignatureAuditEvent.ChainHashLength);
        builder.Property(e => e.ChainHash).IsRequired().HasMaxLength(SignatureAuditEvent.ChainHashLength);

        // (RequestId, Sequence) es único: sirve para detectar reintentos y para el tail lookup.
        builder.HasIndex(e => new { e.SignatureRequestId, e.Sequence }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.OccurredAtUtc });
    }
}
