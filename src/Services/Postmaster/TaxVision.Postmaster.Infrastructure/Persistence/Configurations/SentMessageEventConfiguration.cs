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
        // Se agrega vía la colección de navegación SentMessage._events (fixup), no vía DbSet.Add()
        // directo — sin ValueGeneratedNever() el Guid PK client-generado confunde a EF sobre si debe
        // INSERT o UPDATE en una segunda SaveChangesAsync del mismo DbContext (ver SentMessageConfiguration).
        builder.Property(e => e.Id).ValueGeneratedNever();
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
