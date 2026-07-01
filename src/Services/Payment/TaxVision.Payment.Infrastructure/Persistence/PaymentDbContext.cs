using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Payment.Domain.SaaSPayments;
using TaxVision.Payment.Domain.StripeCustomers;
using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Infrastructure.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<SaaSPayment> SaaSPayments => Set<SaaSPayment>();
    public DbSet<StripeCustomer> StripeCustomers => Set<StripeCustomer>();
    public DbSet<TenantPaymentConfig> TenantPaymentConfigs => Set<TenantPaymentConfig>();
    public DbSet<TenantTransaction> TenantTransactions => Set<TenantTransaction>();

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
                ex);
        }
    }
}
