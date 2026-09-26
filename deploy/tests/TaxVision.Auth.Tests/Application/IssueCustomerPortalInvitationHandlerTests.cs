using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Customers.Commands;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

public sealed class IssueCustomerPortalInvitationHandlerTests
{
    private const string Email = "ana@example.com";

    [Fact]
    public async Task An_employee_can_also_be_invited_as_a_client_with_the_same_email()
    {
        var world = new AccountSessionFixture();
        world.Users.Add(
            User.Register(world.Tenant.Id, "Ana", "Employee", Email, "hash", UserActorType.TenantEmployee).Value
        );
        var context = new Context(world);

        var result = await context.InviteAsync(Guid.NewGuid());

        Assert.Equal(PortalInvitationOutcome.Invited, result.Value.Outcome);
        var invitation = Assert.Single(context.Invitations.Invitations);
        Assert.Equal(UserActorType.CustomerPortal, invitation.ActorType);
        var sent = Assert.Single(context.Bus.Published.OfType<InvitationCreatedIntegrationEvent>());
        Assert.Equal("CustomerPortal", sent.ActorType);
        Assert.False(sent.IsResend);
    }

    [Fact]
    public async Task Asking_again_with_a_pending_invitation_resends_it_with_a_new_link()
    {
        var world = new AccountSessionFixture();
        var context = new Context(world);
        var customerId = Guid.NewGuid();
        await context.InviteAsync(customerId);
        var firstToken = context.Invitations.Invitations.Single().TokenHash;

        var result = await context.InviteAsync(customerId);

        Assert.Equal(PortalInvitationOutcome.Resent, result.Value.Outcome);
        var invitation = Assert.Single(context.Invitations.Invitations);
        Assert.NotEqual(firstToken, invitation.TokenHash);
        Assert.Equal(1, invitation.ResendCount);
        Assert.True(context.Bus.Published.OfType<InvitationCreatedIntegrationEvent>().Last().IsResend);
    }

    [Fact]
    public async Task Resending_stops_at_the_limit()
    {
        var world = new AccountSessionFixture();
        var context = new Context(world);
        var customerId = Guid.NewGuid();
        for (var attempt = 0; attempt <= Invitation.MaxResends; attempt++)
            await context.InviteAsync(customerId);

        var result = await context.InviteAsync(customerId);

        Assert.Equal("Invitation.ResendLimit", result.Error.Code);
    }

    [Fact]
    public async Task The_email_of_another_clients_pending_invitation_is_rejected()
    {
        var world = new AccountSessionFixture();
        var context = new Context(world);
        await context.InviteAsync(Guid.NewGuid());

        var result = await context.InviteAsync(Guid.NewGuid());

        Assert.Equal(IssueCustomerPortalInvitationHandler.EmailInUse, result.Error);
    }

    [Fact]
    public async Task The_email_of_another_clients_portal_account_is_rejected()
    {
        var world = new AccountSessionFixture();
        world.Users.Add(Portal(world, Guid.NewGuid()));
        var context = new Context(world);

        var result = await context.InviteAsync(Guid.NewGuid());

        Assert.Equal(IssueCustomerPortalInvitationHandler.EmailInUse, result.Error);
        Assert.Empty(context.Bus.Published);
    }

    [Fact]
    public async Task A_client_with_active_portal_access_is_reported_without_sending_anything()
    {
        var world = new AccountSessionFixture();
        var customerId = Guid.NewGuid();
        world.Users.Add(Portal(world, customerId));
        var context = new Context(world);

        var result = await context.InviteAsync(customerId);

        Assert.Equal(PortalInvitationOutcome.AlreadyActive, result.Value.Outcome);
        Assert.Empty(context.Bus.Published);
    }

    [Fact]
    public async Task A_client_with_deactivated_portal_access_is_told_to_reactivate_it()
    {
        var world = new AccountSessionFixture();
        var customerId = Guid.NewGuid();
        var portal = Portal(world, customerId);
        portal.Deactivate(DateTime.UtcNow);
        world.Users.Add(portal);
        var context = new Context(world);

        var result = await context.InviteAsync(customerId);

        Assert.Equal(IssueCustomerPortalInvitationHandler.AccessDeactivated, result.Error);
    }

    private static User Portal(AccountSessionFixture world, Guid customerId) =>
        User.Register(world.Tenant.Id, "Ana", "Client", Email, "hash", UserActorType.CustomerPortal, customerId).Value;

    private sealed class Context(AccountSessionFixture world)
    {
        public InMemoryInvitationRepository Invitations { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        private readonly SequentialInvitationTokenService _tokens = new();

        public Task<Result<PortalInvitationResult>> InviteAsync(Guid customerId) =>
            IssueCustomerPortalInvitationHandler.Handle(
                new IssueCustomerPortalInvitationCommand(world.Tenant.Id, customerId, Email, world.Admin.Id),
                world.Users,
                world.Tenants,
                Invitations,
                _tokens,
                world.Audit,
                world.Request,
                world.Correlation,
                Options.Create(new InvitationOptions()),
                world.UnitOfWork,
                Bus,
                CancellationToken.None
            );
    }
}
