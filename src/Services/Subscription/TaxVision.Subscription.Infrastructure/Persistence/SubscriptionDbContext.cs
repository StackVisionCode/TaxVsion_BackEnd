using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence;

public sealed class SubscriptionDbContext(DbContextOptions<SubscriptionDbContext> options)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<TenantSubscription> Subscriptions => Set<TenantSubscription>();

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
