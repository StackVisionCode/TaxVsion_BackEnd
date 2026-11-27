using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Oficina con su Owner, un co-admin, un empleado verificado y un cliente con portal, en memoria.</summary>
internal sealed class OfficeCredentialFixture
{
    public Tenant Tenant { get; }
    public User Owner { get; }
    public User CoAdmin { get; }
    public User Employee { get; }
    public User PortalClient { get; }
    public Guid CustomerId { get; } = Guid.NewGuid();

    public InMemoryUserRepository Users { get; } = new();
    public InMemoryTenantRegistry Tenants { get; } = new();
    public InMemoryCredentialTokenRepository Credentials { get; } = new();
    public PredictableTokenService Tokens { get; } = new();
    public AccountTokenProtectionTests.CapturingAccountTokenProtector Protector { get; } = new();
    public FakeAuthAuditWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeMessageBus Bus { get; } = new();
    public FakeRequestContext Request { get; } = new();
    public FakeCorrelationContext Correlation { get; } = new();
    public FakeNotificationDeliveryClient Deliveries { get; } = new();
    public InMemorySessionRepository Sessions { get; } = new();
    public RecordingAccessTokenDenylist Denylist { get; } = new();
    public RecordingSessionRevocationPublisher Revocations { get; } = new();
    public InMemoryMfaRepository Mfa { get; } = new();
    public SessionAccessCutoff Cutoff => new(Sessions, Denylist, Revocations);
    public CredentialAdministrationOptions Settings { get; } = new();
    public IOptions<CredentialAdministrationOptions> Options => Microsoft.Extensions.Options.Options.Create(Settings);

    public OfficeCredentialFixture()
    {
        Tenant = Tenant.Register(Guid.NewGuid(), "office", "Office", TenantKind.Customer, "America/New_York").Value;
        Tenants.Add(Tenant);
        Owner = Add(UserActorType.TenantAdmin, onboardingId: Guid.NewGuid());
        CoAdmin = Add(UserActorType.TenantAdmin);
        Employee = Add(UserActorType.TenantEmployee);
        PortalClient = Add(UserActorType.CustomerPortal, customerId: CustomerId);
    }

    public User Add(
        UserActorType actorType,
        Guid? tenantId = null,
        Guid? onboardingId = null,
        Guid? customerId = null,
        bool verified = true
    )
    {
        var user = User.Register(
            tenantId ?? Tenant.Id,
            "Sam",
            "Staff",
            $"u-{Guid.NewGuid():N}@example.com",
            "hashed:secret",
            actorType,
            customerId,
            onboardingId
        ).Value;
        if (verified)
            user.VerifyEmail();
        Users.Add(user);
        return user;
    }

    public PasswordResetToken SeedLink(
        User user,
        ResetOrigin origin = ResetOrigin.SelfService,
        Guid? by = null,
        Guid? eventId = null
    )
    {
        var token = PasswordResetToken.Create(
            user.TenantId,
            user.Id,
            $"hash:seed-{Guid.NewGuid():N}",
            null,
            TimeSpan.FromHours(1),
            origin,
            by,
            eventId
        );
        Credentials.PasswordResets.Add(token);
        return token;
    }
}

/// <summary>Notification simulado: responde lo que se le fije, o no responde.</summary>
internal sealed class FakeNotificationDeliveryClient : INotificationDeliveryClient
{
    public NotificationEmailLookup Next { get; set; } = NotificationEmailLookup.Unavailable;
    public List<(Guid TenantId, Guid EventId)> Calls { get; } = [];

    public Task<NotificationEmailLookup> GetEmailStatusAsync(Guid tenantId, Guid eventId, CancellationToken ct)
    {
        Calls.Add((tenantId, eventId));
        return Task.FromResult(Next);
    }
}
