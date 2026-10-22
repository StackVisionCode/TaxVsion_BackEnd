using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class SentMessageRecipientConfiguration : IEntityTypeConfiguration<SentMessageRecipient>
{
    public void Configure(EntityTypeBuilder<SentMessageRecipient> builder)
    {
        builder.ToTable("SentMessageRecipients");
        builder.HasKey(r => r.Id);
        // Se agrega vía la colección de navegación SentMessage._recipients (fixup) — mismo motivo que
        // SentMessageEventConfiguration.
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.SentMessageId).IsRequired();
        builder.Property(r => r.Address).HasMaxLength(320).IsRequired();
        builder.Property(r => r.DisplayName).HasMaxLength(200);
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        builder.Property(r => r.ProviderMessageId).HasMaxLength(200);
        builder.Property(r => r.LastEventAtUtc);
        builder.Property(r => r.ErrorReason).HasMaxLength(500);
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.OutcomeAtUtc);
        builder.Property(r => r.ProviderAcceptance).HasMaxLength(SentMessageRecipient.MaxProviderAcceptanceLength);
        builder.Property(r => r.FirstOpenedAtUtc);
        builder.Property(r => r.LastOpenedAtUtc);
        builder.Property(r => r.OpenCount).IsRequired();
        builder.Property(r => r.FirstClickedAtUtc);
        builder.Property(r => r.ClickCount).IsRequired();
        builder.Property(r => r.UnsubscribedAtUtc);

        builder.HasIndex(r => r.SentMessageId);
        // Correlación por id del proveedor cuando un aviso no trae nuestro Message-ID.
        builder.HasIndex(r => r.ProviderMessageId).HasDatabaseName("IX_SentMessageRecipients_ProviderMessageId");
        builder.HasIndex(r => new
        {
            r.TenantId,
            r.Address,
            r.Status,
        });
    }
}
