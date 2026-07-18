using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.Tenants;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Infrastructure.Persistence;

public sealed class PaymentAppDbContext(DbContextOptions<PaymentAppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<SaaSPayment> SaaSPayments => Set<SaaSPayment>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PaymentAuditEntry> AuditEntries => Set<PaymentAuditEntry>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<TenantProviderCustomer> TenantProviderCustomers => Set<TenantProviderCustomer>();

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
