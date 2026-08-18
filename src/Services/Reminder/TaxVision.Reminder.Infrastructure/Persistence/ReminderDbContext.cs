using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Domain.Permissions;
using TaxVision.Reminder.Domain.RateLimiting;

namespace TaxVision.Reminder.Infrastructure.Persistence;

/// <param name="tenantContext">
/// RBAC Fase 5 — tenant del actor autenticado, poblado por <c>JwtTenantContextMiddleware</c> desde
/// el JWT. Alimenta el <c>HasQueryFilter</c> global fail-closed (safety net EF Core).
/// </param>
public sealed class ReminderDbContext(DbContextOptions<ReminderDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<ReminderAggregate> Reminders => Set<ReminderAggregate>();

    // RBAC Fase 3 — proyecciones locales de permisos. No son parte del dominio de Reminder: las
    // mantiene el bus (consumers de Auth) y las lee ProjectionPermissionsSource para autorizar sin
    // llamar a Auth por HTTP en cada request.
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();

    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

    // RateLimit Fase 4 — plan del tenant, mantenido por el consumer de Subscription. Es la mitad
    // local del escalado de cuotas por tier; el multiplicador por categoría se lee de Subscription
    // por HTTP M2M.
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

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
    /// <see cref="Guid.Empty"/> (0 filas). Los jobs cross-tenant (reconciliación de Quartz,
    /// retención) tienen que pedir <c>IgnoreQueryFilters()</c> explícito o devuelven 0 filas
    /// siempre y parecen sanos.
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
