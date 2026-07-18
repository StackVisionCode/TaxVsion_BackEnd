using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Domain.Idempotency;
using TaxVision.Postmaster.Domain.Projections;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Infrastructure.Persistence;

/// <summary>Contexto de Entity Framework Core responsable de la persistencia del dominio Postmaster.</summary>
public sealed class PostmasterDbContext(DbContextOptions<PostmasterDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<SentMessage> SentMessages => Set<SentMessage>();
    public DbSet<SentMessageRecipient> SentMessageRecipients => Set<SentMessageRecipient>();
    public DbSet<SentMessageEvent> SentMessageEvents => Set<SentMessageEvent>();
    public DbSet<SystemEmailProvider> SystemEmailProviders => Set<SystemEmailProvider>();
    public DbSet<TenantEmailProvider> TenantEmailProviders => Set<TenantEmailProvider>();
    public DbSet<ProviderHealthStatus> ProviderHealthStatuses => Set<ProviderHealthStatus>();
    public DbSet<EmailIdempotency> EmailIdempotencies => Set<EmailIdempotency>();
    public DbSet<SuppressionListEntry> SuppressionListEntries => Set<SuppressionListEntry>();
    public DbSet<TenantOAuthAccount> TenantOAuthAccounts => Set<TenantOAuthAccount>();

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
