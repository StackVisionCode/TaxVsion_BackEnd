using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class IncomingEmailConfiguration : IEntityTypeConfiguration<IncomingEmail>
{
    public void Configure(EntityTypeBuilder<IncomingEmail> builder)
    {
        builder.ToTable("IncomingEmails");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.EmailThreadId).IsRequired();
        builder.Property(x => x.AccountId).IsRequired();

        builder.Property(x => x.ProviderCode).IsRequired().HasMaxLength(IncomingEmail.ProviderCodeMaxLength);
        builder.Property(x => x.ProviderMessageId).IsRequired().HasMaxLength(IncomingEmail.ProviderMessageIdMaxLength);
        builder.Property(x => x.InternetMessageId).HasMaxLength(IncomingEmail.InternetMessageIdMaxLength);
        builder.Property(x => x.InReplyTo).HasMaxLength(IncomingEmail.InReplyToMaxLength);
        builder.Property(x => x.References).HasMaxLength(IncomingEmail.ReferencesMaxLength);
        builder.Property(x => x.From).IsRequired().HasMaxLength(EmailAddress.MaxLength);
        builder.Property(x => x.FromDisplayName).HasMaxLength(IncomingEmail.FromDisplayNameMaxLength);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(IncomingEmail.SubjectMaxLength);
        builder.Property(x => x.Snippet).IsRequired().HasMaxLength(IncomingEmail.SnippetMaxLength);
        builder.Property(x => x.ReceivedAtUtc).IsRequired();
        builder.Property(x => x.BodyStatus).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.BodyFetchedAtUtc);
        builder.Property(x => x.HasAttachments).IsRequired();
        builder.Property(x => x.AttachmentCount).IsRequired();

        // Dedup de Fase 4: un mismo InternetMessageId no puede persistirse dos veces para el
        // mismo tenant. Filtrado porque el campo es nullable (no todos los proveedores lo mandan).
        builder
            .HasIndex(x => new { x.TenantId, x.InternetMessageId })
            .IsUnique()
            .HasFilter("[InternetMessageId] IS NOT NULL")
            .HasDatabaseName("IX_IncomingEmails_TenantId_InternetMessageId_Unique");

        // Inbox por customer, más reciente primero (ReceivedAtUtc DESC per plan §24).
        builder
            .HasIndex(x => new
            {
                x.TenantId,
                x.CustomerId,
                x.ReceivedAtUtc,
            })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_IncomingEmails_TenantId_CustomerId_ReceivedAtUtc");

        // Fase 9 — mensajes de UN hilo, orden cronológico ascendente (ver
        // IIncomingEmailRepository.ListByThreadAsync). Distinto del índice de arriba: ese está
        // pensado para un inbox agregado por customer (todos sus hilos mezclados), este filtra
        // por EmailThreadId, que no participa en ningún índice existente de Fase 3.
        builder
            .HasIndex(x => new
            {
                x.TenantId,
                x.EmailThreadId,
                x.ReceivedAtUtc,
            })
            .HasDatabaseName("IX_IncomingEmails_TenantId_EmailThreadId_ReceivedAtUtc");

        builder
            .HasMany(x => x.Recipients)
            .WithOne()
            .HasForeignKey(r => r.IncomingEmailId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .Metadata.FindNavigation(nameof(IncomingEmail.Recipients))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(x => x.Attachments)
            .WithOne()
            .HasForeignKey(a => a.IncomingEmailId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .Metadata.FindNavigation(nameof(IncomingEmail.Attachments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
