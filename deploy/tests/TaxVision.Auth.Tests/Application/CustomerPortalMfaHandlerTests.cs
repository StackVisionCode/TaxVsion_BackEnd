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
}
