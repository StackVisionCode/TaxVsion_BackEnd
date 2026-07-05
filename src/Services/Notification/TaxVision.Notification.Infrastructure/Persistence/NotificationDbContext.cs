using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Domain.Emailing.Accounts;
using TaxVision.Notification.Domain.Emailing.Campaigns;
using TaxVision.Notification.Domain.Emailing.Configurations;
using TaxVision.Notification.Domain.Emailing.Layouts;
using TaxVision.Notification.Domain.Emailing.Sending;
using TaxVision.Notification.Domain.Emailing.Templates;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<EmailProviderConfiguration> EmailProviderConfigurations => Set<EmailProviderConfiguration>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailTemplateVersion> EmailTemplateVersions => Set<EmailTemplateVersion>();
    public DbSet<EmailLayout> EmailLayouts => Set<EmailLayout>();
    public DbSet<OutboundEmailMessage> OutboundEmailMessages => Set<OutboundEmailMessage>();
    public DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();
    public DbSet<EmailDeliveryLog> EmailDeliveryLogs => Set<EmailDeliveryLog>();
    public DbSet<EmailCampaign> EmailCampaigns => Set<EmailCampaign>();
    public DbSet<EmailCampaignRecipient> EmailCampaignRecipients => Set<EmailCampaignRecipient>();
    public DbSet<EmailAccountConnection> EmailAccountConnections => Set<EmailAccountConnection>();
    public DbSet<EmailFolder> EmailFolders => Set<EmailFolder>();
    public DbSet<EmailSyncedMessage> EmailSyncedMessages => Set<EmailSyncedMessage>();
    public DbSet<EmailMessageAttachment> EmailMessageAttachments => Set<EmailMessageAttachment>();
    public DbSet<EmailSyncLog> EmailSyncLogs => Set<EmailSyncLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "A record with the same unique values already exists.",
                ex
            );
        }
    }
}
