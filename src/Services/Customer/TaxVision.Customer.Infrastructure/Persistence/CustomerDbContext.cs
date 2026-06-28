using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Catalogs;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Infrastructure.Persistence;

public sealed class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<DomainCustomer> Customers => Set<DomainCustomer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CustomerContactPoint> CustomerContactPoints => Set<CustomerContactPoint>();
    public DbSet<CustomerRelation> CustomerRelations => Set<CustomerRelation>();
    public DbSet<CustomerFiscalProfile> CustomerFiscalProfiles => Set<CustomerFiscalProfile>();
    public DbSet<CustomerRelationFiscalProfile> CustomerRelationFiscalProfiles => Set<CustomerRelationFiscalProfile>();
    public DbSet<Occupation> Occupations => Set<Occupation>();
    public DbSet<PrincipalBusinessActivity> PrincipalBusinessActivities => Set<PrincipalBusinessActivity>();

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
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) // Si dos requests intentan crear customers con el mismo email al mismo tiempo, uno gana y el otro tira esa excepción.
        {
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "A record with the same unique values already exists.",
                ex
            );
        }
    }
}
