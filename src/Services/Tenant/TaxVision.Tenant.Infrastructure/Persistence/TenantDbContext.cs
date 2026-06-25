using System.Reflection;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Infrastructure.Persistence;

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<DomainTenant> Tenants => Set<DomainTenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
