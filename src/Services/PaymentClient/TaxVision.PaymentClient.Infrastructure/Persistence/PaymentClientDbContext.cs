using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.Payouts;
using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Infrastructure.Persistence;

public sealed class PaymentClientDbContext(DbContextOptions<PaymentClientDbContext> options)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<TenantPayment> TenantPayments => Set<TenantPayment>();
    public DbSet<TenantPaymentConfig> TenantPaymentConfigs => Set<TenantPaymentConfig>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PaymentAuditEntry> AuditEntries => Set<PaymentAuditEntry>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<PaymentLink> PaymentLinks => Set<PaymentLink>();
    public DbSet<TenantConnectAccount> TenantConnectAccounts => Set<TenantConnectAccount>();
    public DbSet<PayoutSchedule> PayoutSchedules => Set<PayoutSchedule>();
    public DbSet<TenantRecurringPayment> TenantRecurringPayments => Set<TenantRecurringPayment>();

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
