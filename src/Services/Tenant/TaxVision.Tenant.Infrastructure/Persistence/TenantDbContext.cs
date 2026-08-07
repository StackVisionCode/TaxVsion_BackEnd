using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Domain.Permissions;
using TaxVision.Tenant.Domain.RateLimiting;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Infrastructure.Persistence;

/// <summary>
/// H-11 — filtro global fail-closed por <see cref="ITenantOwned"/>, igual que los otros 15 servicios.
/// Alcanza SOLO a las tres proyecciones locales; la tabla <c>Tenants</c> queda fuera por diseño —
/// Tenant ES el registro de tenants, su agregado no es <see cref="ITenantOwned"/> y filtrarlo por
/// "el tenant de la request" no significaría nada.
///
/// <para>
/// Los cuatro lectores de proyección ya usaban <c>IgnoreQueryFilters()</c> con un tenantId explícito
/// del evento/JWT (corren en scopes de Wolverine sin tenant ambiental), así que este filtro es red de
/// seguridad pura: no cambia ninguna consulta existente, pero una consulta nueva que se olvide del
/// <c>.Where(p =&gt; p.TenantId == ...)</c> devuelve cero filas en vez de las de todos los tenants.
/// </para>
/// </summary>
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public DbSet<DomainTenant> Tenants => Set<DomainTenant>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilters(modelBuilder);
        base.OnModelCreating(modelBuilder);
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
                typeof(TenantDbContext).GetProperty(
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
