using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.Billing;
using TaxVision.PaymentApp.Domain.PaymentMethods;
using TaxVision.PaymentApp.Domain.Permissions;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.RateLimiting;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.Tenants;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Infrastructure.Persistence;

/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core). AuditEntries/WebhookEvents deliberadamente NO implementan
/// <see cref="ITenantOwned"/> (el tenant no siempre se conoce al recibir el webhook) — el filtro
/// genérico no los alcanza.
/// </param>
public sealed class PaymentAppDbContext(DbContextOptions<PaymentAppDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<SaaSPayment> SaaSPayments => Set<SaaSPayment>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PaymentAuditEntry> AuditEntries => Set<PaymentAuditEntry>();
    public DbSet<OnboardingPaymentMethodOverride> OnboardingPaymentMethodOverrides =>
        Set<OnboardingPaymentMethodOverride>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<TenantProviderCustomer> TenantProviderCustomers => Set<TenantProviderCustomer>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();
    public DbSet<TenantBillingPreferenceProjection> TenantBillingPreferenceProjections =>
        Set<TenantBillingPreferenceProjection>();

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
    /// <see cref="Guid.Empty"/> (0 filas). Jobs cross-tenant (DunningJob, ExpiringPaymentMethodsJob,
    /// PendingChargeReconciliationJob) usan <c>IgnoreQueryFilters()</c> explícito en su query inicial.
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
            return await SaveReleasingDefaultMethodsFirstAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(
                "Persistence.ConcurrencyConflict",
                "The record changed while the operation was in progress.",
                ex
            );
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

    /// <summary>
    /// EF no ordena los UPDATE por un índice único filtrado: mover el default podía escribir primero el nuevo y chocar
    /// con UX_TenantSavedPaymentMethods_OneDefault. Las filas que dejan de ser default activo se escriben antes, en la
    /// misma transacción.
    /// </summary>
    private async Task<int> SaveReleasingDefaultMethodsFirstAsync(CancellationToken ct)
    {
        var leaving = Database.IsRelational() ? MethodsLeavingDefault() : [];
        if (leaving.Count == 0)
            return await base.SaveChangesAsync(ct);

        if (Database.CurrentTransaction is not null)
        {
            await ReleaseAsync(leaving, ct);
            return await base.SaveChangesAsync(ct);
        }

        await using var transaction = await Database.BeginTransactionAsync(ct);
        await ReleaseAsync(leaving, ct);
        var saved = await base.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return saved;
    }

    private List<TenantSavedPaymentMethod> MethodsLeavingDefault() =>
        ChangeTracker
            .Entries<TenantSavedPaymentMethod>()
            .Where(entry =>
                entry.State == EntityState.Modified
                && entry.Property(method => method.IsDefault).OriginalValue
                && !entry.Property(method => method.IsDetached).OriginalValue
                && (!entry.Entity.IsDefault || entry.Entity.IsDetached)
            )
            .Select(entry => entry.Entity)
            .ToList();

    private async Task ReleaseAsync(List<TenantSavedPaymentMethod> methods, CancellationToken ct)
    {
        foreach (var method in methods)
            await Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.TenantSavedPaymentMethods SET IsDefault = {method.IsDefault}, IsDetached = {method.IsDetached} WHERE Id = {method.Id}",
                ct
            );
    }
}
