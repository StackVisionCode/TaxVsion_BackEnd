using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Domain.Compensations;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Quotes;
using TaxVision.Codes.Domain.Redemptions;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.Usage;
using TaxVision.Growth.Infrastructure.Persistence.Audit;
using TaxVision.Growth.Infrastructure.Persistence.Idempotency;
using TaxVision.Growth.Infrastructure.Persistence.Referrals;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Fraud;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;
using TaxVision.Referrals.Domain.Rewards;
using Wolverine;

namespace TaxVision.Growth.Infrastructure.Persistence;

public sealed class GrowthDbContext(
    DbContextOptions<GrowthDbContext> options,
    ITenantContext tenantContext,
    IMessageBus? messageBus = null
) : DbContext(options), IUnitOfWork
{
    public DbSet<CodeDefinition> CodeDefinitions => Set<CodeDefinition>();
    public DbSet<CodeRuleVersion> CodeRules => Set<CodeRuleVersion>();
    public DbSet<CodeScope> CodeScopes => Set<CodeScope>();
    public DbSet<CodeQuote> CodeQuotes => Set<CodeQuote>();
    public DbSet<CodeReservation> CodeReservations => Set<CodeReservation>();
    public DbSet<CodeRedemption> CodeRedemptions => Set<CodeRedemption>();
    public DbSet<CodeCompensation> CodeCompensations => Set<CodeCompensation>();
    public DbSet<CodeUsageCounter> CodeUsageCounters => Set<CodeUsageCounter>();

    public DbSet<ReferralProgram> ReferralPrograms => Set<ReferralProgram>();
    public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();
    public DbSet<ReferralAttribution> ReferralAttributions => Set<ReferralAttribution>();
    public DbSet<ReferralQualification> ReferralQualifications => Set<ReferralQualification>();
    public DbSet<ReferralRewardCase> ReferralRewardCases => Set<ReferralRewardCase>();
    public DbSet<ReferralRewardAttempt> ReferralRewardAttempts => Set<ReferralRewardAttempt>();
    public DbSet<ReferralFraudReview> ReferralFraudReviews => Set<ReferralFraudReview>();
    public DbSet<ReferralRewardQuotaCounter> ReferralRewardQuotaCounters => Set<ReferralRewardQuotaCounter>();
    public DbSet<ReferralRewardQuotaReservation> ReferralRewardQuotaReservations =>
        Set<ReferralRewardQuotaReservation>();

    public DbSet<ProcessedBusinessMessage> ProcessedBusinessMessages => Set<ProcessedBusinessMessage>();
    public DbSet<GrowthAuditEntry> AuditEntries => Set<GrowthAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilters(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuditEntriesAreAppendOnly();

        try
        {
            // Persistir el estado del agregado ANTES de despachar sus domain events, y recién
            // después publicarlos + limpiarlos. Wolverine corre este SaveChanges dentro de su
            // transacción ambiental (AutoApplyTransactions + WithDbContextAbstraction en
            // Program.cs) sin confirmarla todavía, así que los eventos publicados acá se
            // encolan en el outbox durable (PersistMessagesWithSqlServer) y se entregan de
            // forma atómica al commitear la misma transacción — nunca antes.
            //
            // El orden importa: si base.SaveChangesAsync falla (concurrencia, unique-violation),
            // NO se despacha ni se limpia ningún evento, así que no hay eventos fantasma para un
            // estado que nunca se persistió y el retry de Wolverine vuelve a materializarlos desde
            // el agregado recargado. Despachar/limpiar antes del guardado (bug B-01) los perdía.
            var affected = await base.SaveChangesAsync(cancellationToken);
            await DispatchDomainEventsAsync(cancellationToken);
            return affected;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(
                "Persistence.ConcurrencyConflict",
                "The record changed while the operation was in progress.",
                ex
            );
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

    private void ApplyFailClosedTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
                continue;

            var parameter = Expression.Parameter(entity.ClrType, "entity");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));
            var tenantFilterIdProperty =
                typeof(GrowthDbContext).GetProperty(
                    nameof(TenantFilterId),
                    BindingFlags.Instance | BindingFlags.NonPublic
                ) ?? throw new InvalidOperationException("Tenant filter property was not found.");
            var tenantFilterId = Expression.Property(Expression.Constant(this), tenantFilterIdProperty);
            var filter = Expression.Lambda(Expression.Equal(tenantProperty, tenantFilterId), parameter);

            modelBuilder.Entity(entity.ClrType).HasQueryFilter(filter);
        }
    }

    // Empty is never a valid tenant ID. Using it when no request/message tenant was
    // established makes every ITenantOwned query return zero rows instead of opening
    // cross-tenant access or evaluating ITenantContext.TenantId while it is unset.
    private Guid TenantFilterId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    private void EnsureAuditEntriesAreAppendOnly()
    {
        var invalidEntry = ChangeTracker
            .Entries<GrowthAuditEntry>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (invalidEntry is not null)
            throw new InvalidOperationException("Growth audit entries are append-only.");
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var aggregates = ChangeTracker
                .Entries<AggregateRoot>()
                .Select(entry => entry.Entity)
                .Where(aggregate => aggregate.DomainEvents.Count > 0)
                .ToList();

            if (aggregates.Count == 0)
                return;

            if (messageBus is null)
            {
                throw new InvalidOperationException(
                    "IMessageBus is required to persist aggregate roots with pending domain events."
                );
            }

            foreach (var aggregate in aggregates)
            {
                var domainEvents = aggregate.DomainEvents.ToList();
                aggregate.ClearDomainEvents();

                foreach (var domainEvent in domainEvents)
                    await messageBus.PublishAsync(domainEvent);
            }
        }
    }
}
