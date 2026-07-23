using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Terms;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Infrastructure.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — <paramref name="tenantContext"/> es
/// <c>BuildingBlocks.Tenancy.ITenantContext</c>, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el claim <c>tenant_id</c>. NO confundir con
/// <c>TaxVision.Auth.Api.Common.IResolvedTenantContext</c> (existente, distinto): ese es
/// pre-autenticación, resuelto por Host para saber a qué tenant pertenece un login/registro
/// antes de que exista un JWT — nunca alimenta este filtro EF.
/// </summary>
public sealed class AuthDbContext(
    DbContextOptions<AuthDbContext> options,
    IMessageBus bus,
    ITenantContext tenantContext
) : DbContext(options), IUnitOfWork
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantPlanLimits> TenantPlanLimits => Set<TenantPlanLimits>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<MfaMethod> MfaMethods => Set<MfaMethod>();
    public DbSet<MfaChallenge> MfaChallenges => Set<MfaChallenge>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();
    public DbSet<TenantMfaPolicy> TenantMfaPolicies => Set<TenantMfaPolicy>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PhoneVerificationToken> PhoneVerificationTokens => Set<PhoneVerificationToken>();
    public DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();
    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();
    public DbSet<TenantSubdomainReservation> TenantSubdomainReservations => Set<TenantSubdomainReservation>();
    public DbSet<TenantTermsAcceptance> TenantTermsAcceptances => Set<TenantTermsAcceptance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // RBAC Fase 5 — el filtro NO debe cerrar sobre `tenantContext` directo: EF Core cachea el
    // modelo compilado por tipo de DbContext (no por instancia), así que un Expression.Constant
    // sobre el servicio inyectado quedaría congelado con el valor del PRIMER AuthDbContext jamás
    // construido en el proceso — todas las requests siguientes (con su propio ITenantContext
    // scoped) leerían ese primer tenant para siempre. En cambio, cerrar sobre `this` (la propia
    // instancia de DbContext) sí se reevalúa por-instancia: EF Core reconoce ese patrón y lo trata
    // como parámetro, no como constante congelada — por eso el filtro llama a EffectiveTenantId
    // (miembro de este DbContext) en vez de leer tenantContext directo.
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    // Safety net EF Core: toda entidad ITenantOwned queda filtrada por el tenant del actor
    // autenticado. Fail-closed (mismo criterio que GrowthDbContext): sin tenant en contexto,
    // compara contra Guid.Empty (0 filas) en vez de abrir todo. Jobs de background que necesitan
    // cross-tenant (PermissionsBackfillService, SystemRolePermissionsSyncService,
    // AuthMaintenanceService, TenantDomainBackfillService, PlatformAdmin*Service) usan
    // IgnoreQueryFilters() explícito — ver comentario en cada uno.
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
            // Domain events se despachan ANTES del commit (mismo criterio que Wolverine
            // exige para integration events con outbox, capítulo 11 del libro): así, si un
            // handler local agrega una fila de auditoría o encola un evento de integración,
            // todo entra en la MISMA transacción que el cambio de estado que los originó.
            await DispatchDomainEventsAsync(cancellationToken);
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

    /// <summary>
    /// Drena los domain events de todos los agregados rastreados y los publica
    /// in-process vía Wolverine (nunca registrados a RabbitMQ, así que solo llegan a
    /// handlers locales). Repite hasta que ningún agregado tenga eventos pendientes,
    /// por si un handler local llega a mutar otro agregado con eventos propios.
    /// </summary>
    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        while (true)
        {
            var aggregatesWithEvents = ChangeTracker
                .Entries<AggregateRoot>()
                .Select(entry => entry.Entity)
                .Where(aggregate => aggregate.DomainEvents.Count > 0)
                .ToList();

            if (aggregatesWithEvents.Count == 0)
                break;

            foreach (var aggregate in aggregatesWithEvents)
            {
                var domainEvents = aggregate.DomainEvents.ToList();
                aggregate.ClearDomainEvents();

                foreach (var domainEvent in domainEvents)
                    await bus.PublishAsync(domainEvent);
            }
        }
    }
}
