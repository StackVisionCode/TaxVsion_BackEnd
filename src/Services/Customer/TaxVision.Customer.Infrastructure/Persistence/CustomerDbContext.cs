using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Audit;
using TaxVision.Customer.Domain.Catalogs;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.Employees;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Domain.Permissions;
using TaxVision.Customer.Domain.Relations;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Infrastructure.Persistence;

/// <summary>
/// Contexto de Entity Framework Core responsable de la persistencia del dominio Customer.
/// Expone las entidades administradas por el servicio y actúa como unidad de trabajo para
/// confirmar, en una sola operación, los cambios registrados durante una petición.
/// </summary>
/// <param name="options">
/// Opciones del contexto, incluida la conexión a SQL Server, proporcionadas por el
/// contenedor de dependencias.
/// </param>
/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core).
/// </param>
public sealed class CustomerDbContext(DbContextOptions<CustomerDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    /// <summary>Clientes administrados por el servicio.</summary>
    public DbSet<DomainCustomer> Customers => Set<DomainCustomer>();

    /// <summary>Direcciones asociadas a los clientes.</summary>
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    /// <summary>Medios de contacto de los clientes, como correos electrónicos y teléfonos.</summary>
    public DbSet<CustomerContactPoint> CustomerContactPoints => Set<CustomerContactPoint>();

    /// <summary>Relaciones existentes entre clientes.</summary>
    public DbSet<CustomerRelation> CustomerRelations => Set<CustomerRelation>();

    /// <summary>Perfiles fiscales asociados directamente a clientes.</summary>
    public DbSet<CustomerFiscalProfile> CustomerFiscalProfiles => Set<CustomerFiscalProfile>();

    /// <summary>Perfiles fiscales asociados a relaciones entre clientes.</summary>
    public DbSet<CustomerRelationFiscalProfile> CustomerRelationFiscalProfiles => Set<CustomerRelationFiscalProfile>();

    /// <summary>Catálogo de ocupaciones disponible para personas físicas.</summary>
    public DbSet<Occupation> Occupations => Set<Occupation>();

    /// <summary>Catálogo de actividades económicas principales disponible para empresas.</summary>
    public DbSet<PrincipalBusinessActivity> PrincipalBusinessActivities => Set<PrincipalBusinessActivity>();

    /// <summary>Intentos o trabajos de importación masiva de clientes.</summary>
    public DbSet<CustomerImportAttempt> CustomerImportAttempts => Set<CustomerImportAttempt>();

    /// <summary>Filas individuales procesadas dentro de cada importación de clientes.</summary>
    public DbSet<CustomerImportRow> CustomerImportRows => Set<CustomerImportRow>();

    /// <summary>Rastro de auditoría de acciones sensibles sobre clientes (ej. revelar el tax identifier).</summary>
    public DbSet<CustomerAuditLog> CustomerAuditLogs => Set<CustomerAuditLog>();

    /// <summary>Proyección local de usuarios del staff del tenant, alimentada por eventos de Auth.</summary>
    public DbSet<TenantEmployeeDirectoryEntry> TenantEmployeeDirectoryEntries => Set<TenantEmployeeDirectoryEntry>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

    /// <summary>
    /// Construye el modelo de EF Core aplicando las configuraciones de entidades declaradas
    /// en el ensamblado de Infrastructure.
    /// </summary>
    /// <param name="modelBuilder">
    /// Constructor utilizado para configurar tablas, propiedades, relaciones, índices
    /// y restricciones.
    /// </param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Descubre y aplica las clases que implementan IEntityTypeConfiguration<T>
        // dentro de este ensamblado.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ApplyFailClosedTenantFilter(modelBuilder);

        // Permite que DbContext complete la configuración definida por la clase base.
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
    /// <see cref="Guid.Empty"/> (0 filas). Jobs de background que necesitan cross-tenant
    /// (<c>CustomerImportCleanupHostedService</c>) usan <c>IgnoreQueryFilters()</c> explícito.
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

    /// <summary>
    /// Persiste de forma asíncrona en SQL Server todos los cambios que EF Core está siguiendo.
    /// Convierte las violaciones de unicidad de SQL Server en una
    /// <see cref="ConflictException"/> independiente de la tecnología de persistencia.
    /// </summary>
    /// <param name="cancellationToken">
    /// Token que permite cancelar la escritura en la base de datos.
    /// </param>
    /// <returns>Número de entradas de estado escritas en la base de datos.</returns>
    /// <exception cref="ConflictException">
    /// Se produce cuando SQL Server informa de una clave duplicada mediante los errores
    /// 2601 o 2627.
    /// </exception>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Detecta las entidades agregadas, modificadas o eliminadas y ejecuta
            // los INSERT, UPDATE y DELETE correspondientes.
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // 2601: clave duplicada en un índice único.
            // 2627: violación de una restricción UNIQUE o PRIMARY KEY.
            // La API traduce este conflicto a una respuesta HTTP 409.
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "A record with the same unique values already exists.",
                ex
            );
        }
    }
}
