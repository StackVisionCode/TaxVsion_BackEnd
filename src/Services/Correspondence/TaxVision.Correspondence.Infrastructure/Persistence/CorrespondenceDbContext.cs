using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Audit;
using TaxVision.Correspondence.Domain.Backfill;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Infrastructure.Persistence;

/// <summary>
/// Fase 2 agregó el primer DbSet real (CustomerEmailAddresses, proyección de emails de
/// Customer) más TenantBackfillStates (marca de backfill ya corrido, ver
/// TenantCustomerBackfillService). Fase 3 agrega el modelo de inbox (IncomingEmails +
/// EmailThreads, con sus child tables de recipients/attachments). Fase 4 agrega
/// UnmatchedIncomingEmails (cuarentena/debug del consumer de ingestion). Fase 10 agrega el
/// modelo de compose (Drafts + DraftRecipients). Fase 14 agrega CorrespondenceAuditLogs (rastro
/// mínimo de auditoría, primer uso real desde que el plan lo referencia en la §23).
/// </summary>
public sealed class CorrespondenceDbContext(DbContextOptions<CorrespondenceDbContext> options)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<CustomerEmailAddress> CustomerEmailAddresses => Set<CustomerEmailAddress>();
    public DbSet<TenantBackfillState> TenantBackfillStates => Set<TenantBackfillState>();
    public DbSet<IncomingEmail> IncomingEmails => Set<IncomingEmail>();
    public DbSet<IncomingEmailRecipient> IncomingEmailRecipients => Set<IncomingEmailRecipient>();
    public DbSet<IncomingEmailAttachment> IncomingEmailAttachments => Set<IncomingEmailAttachment>();
    public DbSet<EmailThread> EmailThreads => Set<EmailThread>();
    public DbSet<UnmatchedIncomingEmail> UnmatchedIncomingEmails => Set<UnmatchedIncomingEmail>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<DraftRecipient> DraftRecipients => Set<DraftRecipient>();
    public DbSet<CorrespondenceAuditLog> CorrespondenceAuditLogs => Set<CorrespondenceAuditLog>();

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
