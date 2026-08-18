using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Domain.Items;
using TaxVision.Catalog.Domain.Permissions;
using TaxVision.Catalog.Domain.RateLimiting;

namespace TaxVision.Catalog.Infrastructure.Persistence;

/// <summary>DbContext del servicio Catalog. Filtro global fail-closed por tenant (safety net) sobre toda
/// entidad <see cref="ITenantOwned"/>; traduce violaciones de índice único a <see cref="ConflictException"/>.</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<CatalogItemAttribute> CatalogItemAttributes => Set<CatalogItemAttribute>();
    public DbSet<Category> Categories => Set<Category>();

    // RBAC Fase 7 — proyección local de permisos (mantenida por los eventos de Auth).
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

    // RateLimit Fase 2 — proyección local de plan-code (mantenida por los eventos de Subscription).
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // Ver SmsDbContext/NotesDbContext: se cierra sobre `this` (no sobre el tenantContext inyectado) porque
    // EF cachea el modelo compilado por tipo de DbContext.
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

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
            Expression body = Expression.Equal(tenantProperty, effectiveTenantIdAccess);

            // Combina soft-delete en el MISMO filtro (EF permite un solo query filter por entidad): si la
            // entidad tiene `bool IsDeleted`, se agrega `&& !IsDeleted`.
            var isDeleted = entityType.ClrType.GetProperty("IsDeleted");
            if (isDeleted is not null && isDeleted.PropertyType == typeof(bool))
                body = Expression.AndAlso(body, Expression.Not(Expression.Property(parameter, isDeleted)));

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
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
