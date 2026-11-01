using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Customers.Commands;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

// La oficina ve y resetea el segundo factor del portal de un cliente (vía Customer, M2M).
public sealed class CustomerPortalMfaHandlerTests
{
    [Fact]
    public async Task Reset_removes_every_factor_alerts_the_client_and_audits_who_did_it()
    {
        var world = new AccountSessionFixture();
        var customerId = Guid.NewGuid();
        var portal = Portal(world, customerId);
        world.Users.Add(portal);
        var mfa = new InMemoryMfaRepository();
        mfa.Enroll(portal);
        var bus = new FakeMessageBus();

        var result = await ResetCustomerPortalMfaHandler.Handle(
            new ResetCustomerPortalMfaCommand(world.Tenant.Id, customerId, world.Admin.Id, "corr-1"),
            world.Users,
            mfa,
            world.Audit,
            world.UnitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.Equal(CustomerPortalMfaResetOutcome.Reset, result.Value.Outcome);
        Assert.False(portal.MfaEnabled);
        Assert.Empty(mfa.Methods);
        Assert.Empty(mfa.Codes);
        Assert.All(mfa.Devices, device => Assert.False(device.IsActive));
        var alert = Assert.Single(bus.Published.OfType<SecurityAlertIntegrationEvent>());
        Assert.Equal(portal.Id, alert.UserId);
        Assert.Equal(SecurityAlertType.MfaDisabled, alert.AlertType);
        Assert.Contains("office", alert.DetailsJson);
        Assert.Equal("corr-1", alert.CorrelationId);
    }

    [Fact]
    public async Task Reset_of_a_client_without_two_step_changes_nothing()
    {
        var world = new AccountSessionFixture();
        var customerId = Guid.NewGuid();
        world.Users.Add(Portal(world, customerId));
        var bus = new FakeMessageBus();

        var result = await ResetCustomerPortalMfaHandler.Handle(
            new ResetCustomerPortalMfaCommand(world.Tenant.Id, customerId, world.Admin.Id, "corr-1"),
            world.Users,
            new InMemoryMfaRepository(),
            world.Audit,
            world.UnitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.Equal(CustomerPortalMfaResetOutcome.NotEnabled, result.Value.Outcome);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Reset_of_a_client_without_portal_access_is_rejected()
    {
        var world = new AccountSessionFixture();

        var result = await ResetCustomerPortalMfaHandler.Handle(
            new ResetCustomerPortalMfaCommand(world.Tenant.Id, Guid.NewGuid(), world.Admin.Id, "corr-1"),
            world.Users,
            new InMemoryMfaRepository(),
            world.Audit,
            world.UnitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal(ResetCustomerPortalMfaHandler.NoPortalAccount, result.Error);
    }

    [Fact]
    public async Task Status_says_whether_the_client_has_portal_access_two_step_on_and_if_the_office_requires_it()
    {
        var world = new AccountSessionFixture();
        var customerId = Guid.NewGuid();
        var portal = Portal(world, customerId);
        world.Users.Add(portal);
        var mfa = new InMemoryMfaRepository();
        mfa.Enroll(portal);
        mfa.Policy = TenantMfaPolicy.CreateDefault(world.Tenant.Id);
        mfa.Policy.Update(requireForEmployees: false, requireForCustomerPortal: true, trustedDeviceDays: 30);

        var withAccess = await GetCustomerPortalMfaHandler.Handle(
            new GetCustomerPortalMfaQuery(world.Tenant.Id, customerId),
            world.Users,
            mfa,
            CancellationToken.None
        );
        var withoutAccess = await GetCustomerPortalMfaHandler.Handle(
            new GetCustomerPortalMfaQuery(world.Tenant.Id, Guid.NewGuid()),
            world.Users,
            mfa,
            CancellationToken.None
        );

        Assert.Equal(new CustomerPortalMfaStatus(true, true, true), withAccess.Value);
        Assert.Equal(new CustomerPortalMfaStatus(false, false, false), withoutAccess.Value);
    }

    private static User Portal(AccountSessionFixture world, Guid customerId) =>
        User.Register(
            world.Tenant.Id,
            "Ana",
            "Client",
            "ana@example.com",
            "hash",
            UserActorType.CustomerPortal,
            customerId
        ).Value;

    /// <summary>MFA en memoria con lo justo para enrolar, consultar y quitar factores.</summary>
    private sealed class InMemoryMfaRepository : IMfaRepository
    {
        public List<MfaMethod> Methods { get; } = [];
        public List<RecoveryCode> Codes { get; } = [];
        public List<TrustedDevice> Devices { get; } = [];
        public TenantMfaPolicy? Policy { get; set; }

        public void Enroll(User user)
        {
            var method = MfaMethod.Create(user.TenantId, user.Id, MfaMethodType.Totp, "cipher", null).Value;
            method.Confirm();
            Methods.Add(method);
            Codes.Add(RecoveryCode.Create(user.TenantId, user.Id, "hash-1"));
            Devices.Add(TrustedDevice.Create(user.TenantId, user.Id, "device-hash", "Chrome", TimeSpan.FromDays(30)));
            user.EnableMfa();
        }

        public Task<IReadOnlyList<MfaMethod>> GetMethodsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MfaMethod>>(Methods.Where(m => m.UserId == userId).ToList());

        public Task<MfaMethod?> GetMethodAsync(Guid userId, MfaMethodType type, CancellationToken ct = default) =>
            Task.FromResult(Methods.FirstOrDefault(m => m.UserId == userId && m.Type == type));

        public Task<MfaMethod?> GetMethodByIdAsync(Guid methodId, CancellationToken ct = default) =>
            Task.FromResult(Methods.FirstOrDefault(m => m.Id == methodId));

        public Task AddMethodAsync(MfaMethod method, CancellationToken ct = default)
        {
            Methods.Add(method);
            return Task.CompletedTask;
        }

        public void RemoveMethod(MfaMethod method) => Methods.Remove(method);

        public Task AddChallengeAsync(MfaChallenge challenge, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MfaChallenge?> GetChallengeByTicketHashAsync(string ticketHash, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RecoveryCode>> GetRecoveryCodesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecoveryCode>>(Codes.Where(c => c.UserId == userId).ToList());

        public Task AddRecoveryCodesAsync(IEnumerable<RecoveryCode> codes, CancellationToken ct = default)
        {
            Codes.AddRange(codes);
            return Task.CompletedTask;
        }

        public void RemoveRecoveryCodes(IEnumerable<RecoveryCode> codes)
        {
            foreach (var code in codes.ToList())
                Codes.Remove(code);
        }

        public Task<TrustedDevice?> GetTrustedDeviceByHashAsync(
            string deviceTokenHash,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TrustedDevice>> GetTrustedDevicesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrustedDevice>>(Devices.Where(d => d.UserId == userId).ToList());

        public Task AddTrustedDeviceAsync(TrustedDevice device, CancellationToken ct = default)
        {
            Devices.Add(device);
            return Task.CompletedTask;
        }

        public Task<TenantMfaPolicy?> GetPolicyAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Policy);

        public Task AddPolicyAsync(TenantMfaPolicy policy, CancellationToken ct = default)
        {
            Policy = policy;
            return Task.CompletedTask;
        }
    }
}
