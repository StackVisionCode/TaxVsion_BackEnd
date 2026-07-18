using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Audit;
using TaxVision.Customer.Domain.Catalogs;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Imports;
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
public sealed class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options), IUnitOfWork
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

        // Permite que DbContext complete la configuración definida por la clase base.
        base.OnModelCreating(modelBuilder);
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
