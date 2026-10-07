using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Domain.Permissions;
using TaxVision.Wallet.Domain.RateLimiting;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Infrastructure.Persistence;

/// <summary>
/// F1 (00_Plan_And_Architecture.md §9) — esqueleto clonado de Notes. DbSets de dominio del monedero
/// (<see cref="Wallet"/>/<see cref="LedgerEntry"/>, modelo v3 micros + dos deltas) + las proyecciones de
/// plataforma heredadas del esqueleto: RBAC (User/RolePermissionsProjection) y RateLimit
/// (TenantPlanCodeProjection).
/// </summary>
/// <param name="tenantContext">
/// RBAC — tenant del actor autenticado, poblado por <c>JwtTenantContextMiddleware</c> desde el JWT.
/// Alimenta el <c>HasQueryFilter</c> global fail-closed (safety net EF Core).
/// </param>
public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<Domain.Wallet.Wallet> Wallets => Set<Domain.Wallet.Wallet>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<WalletTopUp> WalletTopUps => Set<WalletTopUp>();
    public DbSet<FundingCredit> FundingCredits => Set<FundingCredit>();
    public DbSet<Domain.Reservations.WalletReservation> Reservations => Set<Domain.Reservations.WalletReservation>();
    public DbSet<Domain.Pricing.PriceBookVersion> PriceBookVersions => Set<Domain.Pricing.PriceBookVersion>();
    public DbSet<Domain.Pricing.PriceRule> PriceRules => Set<Domain.Pricing.PriceRule>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// RBAC — tenant efectivo para el filtro, expuesto como miembro de ESTA instancia de DbContext
    /// (no del servicio inyectado directo): EF Core cachea el modelo compilado por tipo de DbContext,
    /// así que cerrar la expresión del filtro sobre <c>tenantContext</c> (constante externa) la
    /// congelaría con el valor del primer contexto construido en el proceso. Cerrar sobre <c>this</c>
    /// sí se reevalúa por-instancia.
    /// </summary>
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    /// <summary>
    /// Safety net EF Core (defense-in-depth): filtra toda entidad <see cref="ITenantOwned"/> por el
    /// tenant del actor autenticado. Fail-closed — sin tenant en contexto, compara contra
    /// <see cref="Guid.Empty"/> (0 filas). El catálogo de precios versionado (F3) será global de
    /// plataforma (no <see cref="ITenantOwned"/>), así que quedará exento de este filtro.
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
