using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.Permissions;
using TaxVision.Billing.Domain.RateLimiting;

namespace TaxVision.Billing.Infrastructure.Persistence;

/// <summary>
/// DbContext del servicio Billing.
///
/// Filtro global fail-closed por ITenantOwned: dentro de un scope de Wolverine (consumer/job) el
/// ITenantContext ambiental está vacío → el filtro colapsa a Guid.Empty → los repos alcanzables
/// desde ese scope deben usar .IgnoreQueryFilters() con un tenantId EXPLÍCITO.
/// </summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<IssuerProfile> IssuerProfiles => Set<IssuerProfile>();
    public DbSet<InvoicePaymentLink> InvoicePaymentLinks => Set<InvoicePaymentLink>();
    public DbSet<InvoiceNumberSequence> InvoiceNumberSequences => Set<InvoiceNumberSequence>();

    // RateLimit Fase 2 — proyección local de PlanCode por tenant, mantenida por
    // TenantPlanCodeProjectionConsumer.
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    // H-01 — proyecciones de autorización mantenidas por AuthzPermissionsProjectionConsumers.
    public DbSet<AuthzUserPermissionsProjection> AuthzUserPermissionsProjections =>
        Set<AuthzUserPermissionsProjection>();
    public DbSet<AuthzRolePermissionsProjection> AuthzRolePermissionsProjections =>
        Set<AuthzRolePermissionsProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(BillingSchemas.Billing);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilters(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
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
                typeof(BillingDbContext).GetProperty(
                    nameof(TenantFilterId),
                    BindingFlags.Instance | BindingFlags.NonPublic
                ) ?? throw new InvalidOperationException("Tenant filter property was not found.");
            var tenantFilterId = Expression.Property(Expression.Constant(this), tenantFilterIdProperty);
            var filter = Expression.Lambda(Expression.Equal(tenantProperty, tenantFilterId), parameter);

            modelBuilder.Entity(entity.ClrType).HasQueryFilter(filter);
        }
    }

    // Guid.Empty nunca es un tenant válido: sin tenant ambiental (scope de Wolverine) el filtro
    // compara contra Empty → cero filas, en vez de abrir acceso cross-tenant.
    private Guid TenantFilterId => _tenantContext.HasTenant ? _tenantContext.TenantId : Guid.Empty;
}
