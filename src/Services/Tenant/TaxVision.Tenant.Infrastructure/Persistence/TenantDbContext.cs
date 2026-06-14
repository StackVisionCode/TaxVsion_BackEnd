using System.Reflection;
using Microsoft.EntityFrameworkCore;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;
namespace TaxVision.Tenant.Infrastructure.Persistence;
// El DbContext es el Unit of Work de EF Core: agrupa cambios en una transacción.
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options)
 : DbContext(options)
{
    public DbSet<DomainTenant> Tenants => Set<DomainTenant>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Aplica TODAS las IEntityTypeConfiguration de este ensamblado.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
