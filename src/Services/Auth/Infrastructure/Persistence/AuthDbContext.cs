using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
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

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options, IMessageBus bus)
    : DbContext(options),
        IUnitOfWork
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
        base.OnModelCreating(modelBuilder);
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
