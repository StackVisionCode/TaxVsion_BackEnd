using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Consents;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class ConsentEventConfiguration : IEntityTypeConfiguration<ConsentEvent>
{
    public void Configure(EntityTypeBuilder<ConsentEvent> builder)
    {
        builder.ToTable("ConsentEvents");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.SignatureRequestId).IsRequired();
        builder.Property(c => c.SignerId).IsRequired();
        builder.Property(c => c.TextVersion).IsRequired().HasMaxLength(80);
        builder.Property(c => c.TextLanguage).IsRequired().HasMaxLength(ConsentEvent.MaxLanguageLength);
        builder.Property(c => c.TextSnapshot).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(c => c.TextHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.ClientIp).HasMaxLength(ConsentEvent.MaxIpLength);
        builder.Property(c => c.UserAgent).HasMaxLength(ConsentEvent.MaxUserAgentLength);
        builder.Property(c => c.AcceptedAtUtc).IsRequired();

        builder.HasIndex(c => new
        {
            c.TenantId,
            c.SignatureRequestId,
            c.SignerId,
            c.AcceptedAtUtc,
        });
    }
}
