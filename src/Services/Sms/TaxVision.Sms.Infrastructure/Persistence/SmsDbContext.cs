using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.Permissions;
using TaxVision.Sms.Domain.RateLimiting;
using TaxVision.Sms.Domain.Webhooks;

namespace TaxVision.Sms.Infrastructure.Persistence;

/// <summary>DbContext del servicio SMS. Filtro global fail-closed por tenant (safety net) sobre toda
/// entidad <see cref="ITenantOwned"/>; traduce violaciones de índice único a <see cref="ConflictException"/>.</summary>
public sealed class SmsDbContext(DbContextOptions<SmsDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();
    public DbSet<SmsMedia> SmsMedia => Set<SmsMedia>();
    public DbSet<SmsOptOut> SmsOptOuts => Set<SmsOptOut>();
    public DbSet<ProcessedWebhook> ProcessedWebhooks => Set<ProcessedWebhook>();

    // RBAC Fase 7 — proyección local de permisos (mantenida por los eventos de Auth) que consulta
    // ProjectionPermissionsSource para autorizar [HasPermission] sin llamar a Auth en el hot path.
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

    // Ver NotesDbContext: se cierra sobre `this` (no sobre el tenantContext inyectado) porque EF cachea
    // el modelo compilado por tipo de DbContext.
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
