using System.Linq.Expressions;
using BuildingBlocks.Domain;
using BuildingBlocks.EfCore;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Domain.Projections;
using TaxVision.Mobile.Domain.RateLimiting;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Infrastructure.Persistence;

public sealed class MobileDbContext(DbContextOptions<MobileDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<MileageRetentionPolicy> MileageRetentionPolicies => Set<MileageRetentionPolicy>();
    public DbSet<TripPoint> TripPoints => Set<TripPoint>();
    public DbSet<TripOperation> TripOperations => Set<TripOperation>();
    public DbSet<MileageRate> MileageRates => Set<MileageRate>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MobileDbContext).Assembly);
        modelBuilder.ApplyTenantTimeZoneProjection();
        var tenant = Expression.Property(Expression.Constant(this), nameof(EffectiveTenantId));
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
                continue;
            var parameter = Expression.Parameter(entity.ClrType, "e");
            modelBuilder
                .Entity(entity.ClrType)
                .HasQueryFilter(
                    Expression.Lambda(
                        Expression.Equal(Expression.Property(parameter, nameof(ITenantOwned.TenantId)), tenant),
                        parameter
                    )
                );
        }
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseUtcDateTimes();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await base.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(
                "Persistence.ConcurrencyConflict",
                "The resource changed. Reload and retry.",
                ex
            );
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "The operation conflicts with an existing resource or sequence.",
                ex
            );
        }
    }
}
