using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Permissions;
using TaxVision.Connectors.Domain.Sync;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Infrastructure.Persistence;

/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core). Solo <see cref="TenantEmailAccount"/> implementa
/// <see cref="ITenantOwned"/> — las otras 7 entidades cuelgan de ella por AccountId (sin FK
/// declarada) y no tienen columna TenantId propia, así que el filtro no las alcanza.
/// </param>
public sealed class ConnectorsDbContext(DbContextOptions<ConnectorsDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<TenantEmailAccount> TenantEmailAccounts => Set<TenantEmailAccount>();
    public DbSet<OAuthConnection> OAuthConnections => Set<OAuthConnection>();
    public DbSet<OAuthToken> OAuthTokens => Set<OAuthToken>();
    public DbSet<ImapCredentials> ImapCredentials => Set<ImapCredentials>();
    public DbSet<SmtpCredentials> SmtpCredentials => Set<SmtpCredentials>();
    public DbSet<ProviderWatchSubscription> ProviderWatchSubscriptions => Set<ProviderWatchSubscription>();
    public DbSet<ProviderSyncCursor> ProviderSyncCursors => Set<ProviderSyncCursor>();
    public DbSet<ProviderConnectionAuditLog> ProviderConnectionAuditLogs => Set<ProviderConnectionAuditLog>();
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
    /// <see cref="Guid.Empty"/> (0 filas). Los lookups system-level de TenantEmailAccount
    /// (GetByIdAsync/GetByEmailAddressAsync/ListActiveAsync, usados por webhooks y background
    /// jobs) usan <c>IgnoreQueryFilters()</c> explícito — ver TenantEmailAccountRepository.
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
