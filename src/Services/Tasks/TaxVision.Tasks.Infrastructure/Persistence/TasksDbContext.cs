using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Tasks.Domain.Backfill;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Labels;
using TaxVision.Tasks.Domain.Permissions;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Domain.RateLimiting;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Infrastructure.Persistence;

/// <param name="tenantContext">
/// Tenant del actor autenticado, poblado desde el JWT. Alimenta el filtro global fail-closed.
/// </param>
public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    // Catálogo de presentación por tenant. El motor nunca lo lee: lee TaskItemStatus.
    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();

    // La regla, no las tareas: cada serie tiene a lo sumo una instancia abierta en Tasks.
    public DbSet<TaskSeries> TaskSeries => Set<TaskSeries>();

    public DbSet<TaskTemplate> TaskTemplates => Set<TaskTemplate>();

    public DbSet<ClientRequest> ClientRequests => Set<ClientRequest>();

    // Nadie las escribe desde Task: las mantiene el bus y las lee ProjectionPermissionsSource.
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();

    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

    // La mantiene TenantPlanCodeProjectionConsumer con lo que publica Subscription.
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    public DbSet<CustomerDirectoryEntry> CustomerDirectoryEntries => Set<CustomerDirectoryEntry>();

    // Marca de backfill ya corrido, una fila por tenant descubierto.
    public DbSet<TenantBackfillState> TenantBackfillStates => Set<TenantBackfillState>();

    /// <summary>
    /// SQL Server devuelve <c>datetime2</c> con <see cref="DateTimeKind.Unspecified"/>, así que una
    /// fecha guardada en UTC vuelve sin serlo. El dominio sí lo exige —<c>RecurrenceRule.NextAfter</c>
    /// rechaza cualquier semilla que no sea UTC— y la serie se quedaba sin materializar la siguiente
    /// ocurrencia en silencio. Todas las columnas de fecha de este servicio son UTC; se marca al leer.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
        base.ConfigureConventions(builder);
    }

    private sealed class UtcDateTimeConverter()
        : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class UtcNullableDateTimeConverter()
        : ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
        );

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Miembro de esta instancia, no del servicio inyectado: EF cachea el modelo compilado por tipo
    /// de DbContext, así que cerrar el filtro sobre <c>tenantContext</c> lo congelaría con el valor
    /// del primer contexto del proceso. Cerrar sobre <c>this</c> se reevalúa por instancia.
    /// </summary>
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    /// <summary>
    /// Filtra toda entidad <see cref="ITenantOwned"/> por el tenant del actor. Fail-closed: sin
    /// tenant en contexto compara contra <see cref="Guid.Empty"/> y devuelve 0 filas, así que los
    /// jobs cross-tenant tienen que pedir <c>IgnoreQueryFilters()</c> explícito.
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
