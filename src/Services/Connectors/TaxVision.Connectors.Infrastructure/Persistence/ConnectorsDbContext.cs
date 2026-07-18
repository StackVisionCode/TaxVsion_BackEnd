using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Sync;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Infrastructure.Persistence;

public sealed class ConnectorsDbContext(DbContextOptions<ConnectorsDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<TenantEmailAccount> TenantEmailAccounts => Set<TenantEmailAccount>();
    public DbSet<OAuthConnection> OAuthConnections => Set<OAuthConnection>();
    public DbSet<OAuthToken> OAuthTokens => Set<OAuthToken>();
    public DbSet<ImapCredentials> ImapCredentials => Set<ImapCredentials>();
    public DbSet<SmtpCredentials> SmtpCredentials => Set<SmtpCredentials>();
    public DbSet<ProviderWatchSubscription> ProviderWatchSubscriptions => Set<ProviderWatchSubscription>();
    public DbSet<ProviderSyncCursor> ProviderSyncCursors => Set<ProviderSyncCursor>();
    public DbSet<ProviderConnectionAuditLog> ProviderConnectionAuditLogs => Set<ProviderConnectionAuditLog>();

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
