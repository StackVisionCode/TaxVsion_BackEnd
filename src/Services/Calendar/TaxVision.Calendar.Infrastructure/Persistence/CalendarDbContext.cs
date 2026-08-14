using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Backfill;
using TaxVision.Calendar.Domain.Feeds;
using TaxVision.Calendar.Domain.Permissions;
using TaxVision.Calendar.Domain.Projections;
using TaxVision.Calendar.Domain.RateLimiting;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Infrastructure.Persistence;

/// <param name="tenantContext">
/// Tenant del actor autenticado, poblado desde el JWT. Alimenta el filtro global fail-closed.
/// </param>
public sealed class CalendarDbContext(DbContextOptions<CalendarDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    /// <summary>
    /// Las excepciones se cargan siempre con su serie, pero se declaran igual: sin el DbSet, una
    /// consulta directa las veria sin el filtro de tenant.
    /// </summary>
    public DbSet<AppointmentException> AppointmentExceptions => Set<AppointmentException>();

    /// <summary>Nadie las escribe desde Calendar: las mantiene el bus y las lee la autorizacion.</summary>
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();

    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

    public DbSet<AppointmentType> AppointmentTypes => Set<AppointmentType>();

    public DbSet<AvailabilityRule> AvailabilityRules => Set<AvailabilityRule>();

    public DbSet<BlockedTime> BlockedTimes => Set<BlockedTime>();

    public DbSet<CustomerDirectoryEntry> CustomerDirectoryEntries => Set<CustomerDirectoryEntry>();

    public DbSet<CalendarFeedToken> CalendarFeedTokens => Set<CalendarFeedToken>();

    /// <summary>Marca de backfill ya corrido, una fila por tenant descubierto.</summary>
    public DbSet<TenantBackfillState> TenantBackfillStates => Set<TenantBackfillState>();

    /// <summary>La mantiene el consumer con lo que publica Subscription.</summary>
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    /// <summary>
    /// SQL Server devuelve <c>datetime2</c> con <see cref="DateTimeKind.Unspecified"/>, asi que una
    /// fecha guardada en UTC vuelve sin serlo. En un servicio de calendario eso es fatal: el
    /// invariante que exige UTC rechaza su propio dato al releerlo, y la unica pista es una Z que
    /// falta en la respuesta. Todas las columnas de fecha de este servicio son UTC; se marca al leer.
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
    /// de DbContext, asi que cerrar el filtro sobre <c>tenantContext</c> lo congelaria con el valor
    /// del primer contexto del proceso. Cerrar sobre <c>this</c> se reevalua por instancia.
    /// </summary>
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    /// <summary>
    /// Filtra toda entidad <see cref="ITenantOwned"/> por el tenant del actor. Fail-closed: sin
    /// tenant en contexto compara contra <see cref="Guid.Empty"/> y devuelve 0 filas, asi que los
    /// jobs cross-tenant tienen que pedir <c>IgnoreQueryFilters()</c> explicito.
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
