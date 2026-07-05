using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class EmailAccountConnectionConfiguration : IEntityTypeConfiguration<EmailAccountConnection>
{
    public void Configure(EntityTypeBuilder<EmailAccountConnection> builder)
    {
        builder.ToTable("EmailAccountConnections");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.Provider).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(a => a.EmailAddress).HasMaxLength(320).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(200);
        builder.Property(a => a.ExternalAccountId).HasMaxLength(256);
        builder.Property(a => a.AccessTokenCipher).HasMaxLength(2048);
        builder.Property(a => a.RefreshTokenCipher).HasMaxLength(2048);
        builder.Property(a => a.ImapHost).HasMaxLength(255);
        builder.Property(a => a.ImapUsername).HasMaxLength(320);
        builder.Property(a => a.ImapPasswordCipher).HasMaxLength(2048);
        builder.Property(a => a.SyncStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.LastError).HasMaxLength(1024);
        builder.Property(a => a.CreatedAtUtc).IsRequired();

        builder.HasIndex(a => a.TenantId);
        builder.HasIndex(a => new { a.IsActive, a.LastSyncAtUtc });
    }
}

public sealed class EmailFolderConfiguration : IEntityTypeConfiguration<EmailFolder>
{
    public void Configure(EntityTypeBuilder<EmailFolder> builder)
    {
        builder.ToTable("EmailFolders");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.AccountId).IsRequired();
        builder.Property(f => f.ExternalId).HasMaxLength(512).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(512).IsRequired();
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(f => f.SyncCursor).HasMaxLength(512);
        builder.Property(f => f.CreatedAtUtc).IsRequired();

        builder.HasIndex(f => new { f.AccountId, f.ExternalId }).IsUnique();
    }
}

public sealed class EmailSyncedMessageConfiguration : IEntityTypeConfiguration<EmailSyncedMessage>
{
    public void Configure(EntityTypeBuilder<EmailSyncedMessage> builder)
    {
        builder.ToTable("EmailSyncedMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.AccountId).IsRequired();
        builder.Property(m => m.FolderId).IsRequired();
        builder.Property(m => m.ExternalMessageId).HasMaxLength(512).IsRequired();
        builder.Property(m => m.ExternalThreadId).HasMaxLength(512);
        builder.Property(m => m.Subject).HasMaxLength(500);
        builder.Property(m => m.FromAddress).HasMaxLength(320);
        builder.Property(m => m.ToJson).IsRequired();
        builder.Property(m => m.CcJson).IsRequired();
        builder.Property(m => m.BccJson).IsRequired();
        builder.Property(m => m.Snippet).HasMaxLength(512);
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        builder.HasIndex(m => new { m.AccountId, m.ExternalMessageId }).IsUnique();
        builder.HasIndex(m => new { m.AccountId, m.FolderId, m.ReceivedAtUtc });
        builder.HasIndex(m => new { m.AccountId, m.ExternalThreadId });
    }
}

public sealed class EmailMessageAttachmentConfiguration : IEntityTypeConfiguration<EmailMessageAttachment>
{
    public void Configure(EntityTypeBuilder<EmailMessageAttachment> builder)
    {
        builder.ToTable("EmailMessageAttachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.MessageId).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(512).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(128);
        builder.Property(a => a.ExternalAttachmentId).HasMaxLength(512);
        builder.Property(a => a.CreatedAtUtc).IsRequired();

        builder.HasIndex(a => a.MessageId);
    }
}

public sealed class EmailSyncLogConfiguration : IEntityTypeConfiguration<EmailSyncLog>
{
    public void Configure(EntityTypeBuilder<EmailSyncLog> builder)
    {
        builder.ToTable("EmailSyncLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.AccountId).IsRequired();
        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(l => l.StartedAtUtc).IsRequired();
        builder.Property(l => l.Error).HasMaxLength(1024);

        builder.HasIndex(l => new { l.AccountId, l.StartedAtUtc });
    }
}
