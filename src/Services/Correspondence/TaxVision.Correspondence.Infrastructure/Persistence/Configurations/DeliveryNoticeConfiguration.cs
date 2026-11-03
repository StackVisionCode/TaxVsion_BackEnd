using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Delivery;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryNoticeConfiguration : IEntityTypeConfiguration<DeliveryNotice>
{
    public void Configure(EntityTypeBuilder<DeliveryNotice> builder)
    {
        builder.ToTable("DeliveryNotices");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.EmailThreadId).IsRequired();
        builder.Property(x => x.DraftId).IsRequired();
        builder.Property(x => x.SentMessageId).IsRequired();
        builder.Property(x => x.CustomerId);
        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.Recipient).IsRequired().HasMaxLength(DeliveryNotice.RecipientMaxLength);
        builder.Property(x => x.Outcome).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Category).HasMaxLength(DeliveryNotice.CategoryMaxLength);
        builder.Property(x => x.StatusCode).HasMaxLength(DeliveryNotice.StatusCodeMaxLength);
        builder.Property(x => x.Diagnostic).HasMaxLength(DeliveryNotice.DiagnosticMaxLength);
        builder.Property(x => x.Suppressed).IsRequired();
        builder.Property(x => x.ReportAccountId);
        builder.Property(x => x.ReportProviderMessageId).HasMaxLength(DeliveryNotice.ReportProviderMessageIdMaxLength);
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.ReceivedAtUtc).IsRequired();
        builder.Property(x => x.IsRead).IsRequired();
        builder.Property(x => x.ReadAtUtc);

        // Idempotencia: Postmaster publica uno por cambio de resultado con EventId aleatorio. También frena dos
        // entregas concurrentes del mismo evento.
        builder
            .HasIndex(x => new
            {
                x.TenantId,
                x.SentMessageId,
                x.Recipient,
                x.Outcome,
            })
            .IsUnique()
            .HasDatabaseName("IX_DeliveryNotices_TenantId_SentMessageId_Recipient_Outcome_Unique");

        // Avisos de una conversación en orden (thread unificado y último aviso de la fila del Inbox).
        builder
            .HasIndex(x => new
            {
                x.TenantId,
                x.EmailThreadId,
                x.OccurredAtUtc,
            })
            .HasDatabaseName("IX_DeliveryNotices_TenantId_EmailThreadId_OccurredAtUtc");

        // Conteo de no leídos (badge e Inbox): solo los avisos sin leer.
        builder
            .HasIndex(x => new { x.TenantId, x.EmailThreadId })
            .HasFilter("[IsRead] = 0")
            .HasDatabaseName("IX_DeliveryNotices_TenantId_EmailThreadId_Unread");
    }
}
