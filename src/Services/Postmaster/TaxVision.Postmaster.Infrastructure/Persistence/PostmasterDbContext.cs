using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Domain.Idempotency;
using TaxVision.Postmaster.Domain.Permissions;
using TaxVision.Postmaster.Domain.Projections;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Infrastructure.Persistence;

/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core). <see cref="SystemEmailProvider"/>/<see cref="ProviderHealthStatus"/>
/// (cross-tenant por diseño, ver su propio doc-comment) y <see cref="EmailIdempotency"/>/
/// <see cref="SuppressionListEntry"/>/<see cref="TenantOAuthAccount"/> (llevan TenantId propio pero
/// no implementan <see cref="ITenantOwned"/> — sus repos siempre filtran explícito por tenant en
/// cada método, verificado antes de esta fase) deliberadamente NO son alcanzados por el filtro.
/// </param>
public sealed class PostmasterDbContext(DbContextOptions<PostmasterDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<SentMessage> SentMessages => Set<SentMessage>();
    public DbSet<SentMessageRecipient> SentMessageRecipients => Set<SentMessageRecipient>();
    public DbSet<SentMessageEvent> SentMessageEvents => Set<SentMessageEvent>();
    public DbSet<SystemEmailProvider> SystemEmailProviders => Set<SystemEmailProvider>();
    public DbSet<TenantEmailProvider> TenantEmailProviders => Set<TenantEmailProvider>();
    public DbSet<ProviderHealthStatus> ProviderHealthStatuses => Set<ProviderHealthStatus>();
    public DbSet<EmailIdempotency> EmailIdempotencies => Set<EmailIdempotency>();
    public DbSet<SuppressionListEntry> SuppressionListEntries => Set<SuppressionListEntry>();
    public DbSet<TenantOAuthAccount> TenantOAuthAccounts => Set<TenantOAuthAccount>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
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
    /// <see cref="Guid.Empty"/> (0 filas). Alcanza <c>SentMessage</c>/<c>SentMessageRecipient</c>/
    /// <c>SentMessageEvent</c>/<c>TenantEmailProvider</c> y, vía <c>TenantEntity</c>, también
    /// <c>UserPermissionsProjection</c>/<c>RolePermissionsProjection</c> (RBAC Fase 7) — los 6
    /// <see cref="ITenantOwned"/> reales de este contexto (nota corregida 2026-07-22: la cuenta de
    /// "4" de esta nota era vieja, de antes de Fase 7). Todos sus repos filtran por tenant explícito;
    /// los métodos invocados desde handlers Wolverine (sin <c>TenantContext</c> ambiente confiable,
    /// ver <c>LocalCommandTenantMiddleware</c>) ya llevan <c>IgnoreQueryFilters()</c> explícito —
    /// auditoría RBAC Fase 5/7 (2026-07-22) confirmó y cerró los últimos 4 gaps
    /// (<c>SentMessageRepository.GetByIdWithEventsAsync</c>, <c>TenantEmailProviderRepository.GetByCodeAsync</c>/
    /// <c>GetEnabledByTenantAsync</c>, <c>UserPermissionsProjectionRepository.GetSnapshotAsync</c>).
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
