using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Legal;
using TaxVision.CloudStorage.Domain.Permissions;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Domain.Moderation;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.RateLimiting;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Infrastructure.Persistence;

/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core).
/// </param>
public sealed class CloudStorageDbContext(DbContextOptions<CloudStorageDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<FileObject> Files => Set<FileObject>();
    public DbSet<TenantStorageLimit> StorageLimits => Set<TenantStorageLimit>();
    public DbSet<TenantCloudConnection> CloudConnections => Set<TenantCloudConnection>();
    public DbSet<TenantStoragePolicy> StoragePolicies => Set<TenantStoragePolicy>();
    public DbSet<CloudProviderOAuthState> CloudProviderOAuthStates => Set<CloudProviderOAuthState>();
    public DbSet<TenantCloudFileCopy> CloudFileCopies => Set<TenantCloudFileCopy>();
    public DbSet<StorageAccessLog> AccessLogs => Set<StorageAccessLog>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<DmcaNotice> DmcaNotices => Set<DmcaNotice>();
    public DbSet<CustomerDocumentAccessRule> CustomerDocumentAccessRules => Set<CustomerDocumentAccessRule>();
    public DbSet<CustomerAccessRuleAudit> CustomerAccessRuleAudits => Set<CustomerAccessRuleAudit>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

    // Topes de tamaño administrados por PlatformAdmin (D-33). No tenant-owned: fuera del filtro fail-closed.
    public DbSet<UploadSizePolicy> UploadSizePolicies => Set<UploadSizePolicy>();
    public DbSet<UploadSizePolicyAudit> UploadSizePolicyAudits => Set<UploadSizePolicyAudit>();

    // Política de moderación de contenido (T-20): una fila de plataforma, fuera del filtro fail-closed.
    public DbSet<ContentModerationPolicy> ContentModerationPolicies => Set<ContentModerationPolicy>();
    public DbSet<ContentModerationPolicyAudit> ContentModerationPolicyAudits => Set<ContentModerationPolicyAudit>();

    // RateLimit Fase 2 — proyección local de "¿qué PlanCode tiene este tenant hoy?", mantenida por
    // TenantPlanCodeProjectionConsumer.
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    // Proyección compartida de asignaciones cliente→staff (kit BuildingBlocks.CustomerVisibility),
    // mantenida por el consumer compartido + la reconciliación. Alimenta el filtro ABAC de visibilidad.
    public DbSet<CustomerAssignmentProjection> CustomerAssignmentProjections => Set<CustomerAssignmentProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyCustomerAssignmentProjection();
        ApplyFailClosedTenantFilter(modelBuilder);
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
    /// <see cref="Guid.Empty"/> (0 filas). Jobs de background cross-tenant y el endpoint público
    /// de resolución de ShareLink usan <c>IgnoreQueryFilters()</c> explícito.
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

    /// <summary>Reintentos al fusionar contadores de cuota que chocaron con otra escritura.</summary>
    internal const int MaxQuotaMergeAttempts = 5;

    private static readonly string[] QuotaCounters = [nameof(TenantStorageLimit.UsedBytes), nameof(TenantStorageLimit.ReservedBytes)];

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
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
            // Dos escaneos/subidas en paralelo mueven la cuota del mismo tenant: solo chocan los contadores.
            // Se relee la fila y se aplican encima los mismos incrementos; EF deshace el intento fallido.
            catch (DbUpdateConcurrencyException ex)
                when (attempt < MaxQuotaMergeAttempts && ex.Entries.All(e => e.Entity is TenantStorageLimit))
            {
                foreach (var entry in ex.Entries)
                    await MergeQuotaCountersAsync(entry, cancellationToken);
            }
        }
    }

    private static async Task MergeQuotaCountersAsync(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        CancellationToken cancellationToken
    )
    {
        var deltas = QuotaCounters.ToDictionary(
            name => name,
            name => (long)entry.Property(name).CurrentValue! - (long)entry.Property(name).OriginalValue!
        );
        var database =
            await entry.GetDatabaseValuesAsync(cancellationToken)
            ?? throw new InvalidOperationException("Tenant quota row was deleted while it was being updated.");
        // Lo que no son contadores se toma de la base salvo que este contexto lo haya cambiado.
        foreach (var property in entry.Properties.Where(p => !QuotaCounters.Contains(p.Metadata.Name)))
        {
            if (!property.IsModified)
                property.CurrentValue = database[property.Metadata.Name];
        }
        entry.OriginalValues.SetValues(database);
        foreach (var (name, delta) in deltas)
        {
            var property = entry.Property(name);
            property.CurrentValue = Math.Max(0, (long)database[name]! + delta);
            property.IsModified = true;
        }
    }
}
