using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Audit;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Permissions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence;

/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core). <see cref="SubscriptionPlan"/>/<see cref="AddOnDefinition"/>
/// (catálogo global, no ITenantOwned) y <see cref="SubscriptionAuditLog"/>/
/// <see cref="TenantEntitlementSnapshot"/> (llevan TenantId propio pero no implementan
/// <see cref="ITenantOwned"/>) deliberadamente NO son alcanzados por el filtro.
/// </param>
public sealed class SubscriptionDbContext(DbContextOptions<SubscriptionDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<SubscriptionPlan> Plans => Set<SubscriptionPlan>();
    public DbSet<TenantSubscription> Subscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionSeat> Seats => Set<SubscriptionSeat>();
    public DbSet<SubscriptionTenantSettings> TenantSettings => Set<SubscriptionTenantSettings>();
    public DbSet<AddOnDefinition> AddOnDefinitions => Set<AddOnDefinition>();
    public DbSet<TenantAddOn> TenantAddOns => Set<TenantAddOn>();
    public DbSet<TenantEntitlementSnapshot> EntitlementSnapshots => Set<TenantEntitlementSnapshot>();
    public DbSet<SubscriptionAuditLog> AuditLogs => Set<SubscriptionAuditLog>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// RBAC Fase 5 — tenant efectivo para el filtro, expuesto como miembro de ESTA instancia de
    /// DbContext (no del servicio inyectado directo): EF Core cachea el modelo compilado por tipo
    /// de DbContext, así que cerrar la expresión del filtro sobre <c>tenantContext</c> (constante
    /// externa) la congelaría con el valor del primer contexto construido en el proceso. Cerrar
    /// sobre <c>this</c> sí se reevalúa por-instancia.
    /// </summary>
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    /// <summary>
    /// Safety net EF Core (defense-in-depth): filtra toda entidad <see cref="ITenantOwned"/> por
    /// el tenant del actor autenticado. Fail-closed — sin tenant en contexto, compara contra
    /// <see cref="Guid.Empty"/> (0 filas). Alcanza <c>TenantSubscription</c>/<c>SubscriptionSeat</c>/
    /// <c>TenantAddOn</c>/<c>SubscriptionTenantSettings</c> — los 4 jobs de renovación/expiración
    /// cross-tenant y las 2 queries admin (GetPastDueAsync/GetExpiredAsync) usan
    /// <c>IgnoreQueryFilters()</c> explícito en su query inicial, y los primeros además sellan
    /// <c>bus.TenantId</c> antes de despachar <c>RecalculateEntitlementsCommand</c> por-item.
    /// </summary>
    private void ApplyFailClosedTenantFilter(ModelBuilder modelBuilder)
    {
        var contextConstant = Expression.Constant(this);
        var effectiveTenantIdAccess = Expression.Property(contextConstant, nameof(EffectiveTenantId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));

            var filter = Expression.Lambda(Expression.Equal(tenantProperty, effectiveTenantIdAccess), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
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
