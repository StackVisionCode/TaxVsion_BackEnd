using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Invitations.Queries;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Validación anónima por token para la página de aceptar invitación: reporta el estado efectivo y la
/// oficina del token (para branding), sin mutar. Un token desconocido → "Invalid"; un Pending vencido → "Expired".</summary>
public sealed class ValidateInvitationHandlerTests
{
    private const string RawToken = "raw-token";
    private const string HashValue = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Pending_returns_status_email_actor_and_tenant_brand()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Manfer", "manfer", TenantKind.Customer, "America/Santo_Domingo").Value;
        var invitation = Pending(tenantId, "newhire@example.com", UserActorType.TenantEmployee);

        var result = await ValidateInvitationHandler.Handle(
            new ValidateInvitationQuery(RawToken),
            new FakeInvitationRepo(invitation),
            new FakeTokenService(),
            new FakeTenantRegistry(tenant),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Pending", result.Value.Status);
        Assert.Equal("newhire@example.com", result.Value.Email);
        Assert.Equal("TenantEmployee", result.Value.ActorType);
        Assert.NotNull(result.Value.Tenant);
        Assert.Equal("manfer", result.Value.Tenant!.SubDomain);
        Assert.Equal(tenantId, result.Value.Tenant.Id);
    }

    [Fact]
    public async Task Unknown_token_returns_Invalid_without_tenant()
    {
        var result = await ValidateInvitationHandler.Handle(
            new ValidateInvitationQuery(RawToken),
            new FakeInvitationRepo(null),
            new FakeTokenService(),
            new FakeTenantRegistry(null),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Invalid", result.Value.Status);
        Assert.Null(result.Value.Tenant);
        Assert.Null(result.Value.Email);
    }

    [Fact]
    public async Task Accepted_invitation_reports_Accepted()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Manfer", "manfer", TenantKind.Customer, "America/Santo_Domingo").Value;
        var invitation = Pending(tenantId, "used@example.com", UserActorType.CustomerPortal, Guid.NewGuid());
        invitation.Accept(Guid.NewGuid(), DateTime.UtcNow);

        var result = await ValidateInvitationHandler.Handle(
            new ValidateInvitationQuery(RawToken),
            new FakeInvitationRepo(invitation),
            new FakeTokenService(),
            new FakeTenantRegistry(tenant),
            CancellationToken.None
        );

        Assert.Equal("Accepted", result.Value.Status);
        Assert.Equal("CustomerPortal", result.Value.ActorType);
    }

    private static Invitation Pending(Guid tenantId, string email, UserActorType actorType, Guid? customerId = null) =>
        Invitation
            .Create(
                tenantId,
                email,
                actorType,
                customerId: actorType == UserActorType.CustomerPortal ? customerId ?? Guid.NewGuid() : null,
                invitedByUserId: Guid.NewGuid(),
                tokenHash: HashValue,
                expiresAtUtc: DateTime.UtcNow.AddDays(1),
                roleIdsJson: null
            )
            .Value;

    private sealed class FakeInvitationRepo(Invitation? invitation) : IInvitationRepository
    {
        public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(invitation);

        public Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPendingAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Invitation invitation, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CountPendingAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<Invitation> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            InvitationStatus? status,
            int page,
            int size,
            Guid? customerId = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeTokenService : IInvitationTokenService
    {
        public InvitationToken Generate() => throw new NotSupportedException();

        public string Hash(string rawToken) => HashValue;
    }

    private sealed class FakeTenantRegistry(Tenant? tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(tenant);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetBillingBlockedAsync(
            Guid tenantId,
            bool blocked,
            string? reason,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
