using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Panel "Sign-in security" del empleado (03 §14.1, §15): sin token ni link; entrega Unknown hasta F3-L5.</summary>
public sealed class GetStaffCredentialStatusHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private Task<Result<StaffCredentialStatusResponse>> GetAsync(User actor, User target) =>
        GetStaffCredentialStatusHandler.Handle(
            new GetStaffCredentialStatusQuery(_office.Tenant.Id, actor.Id, target.Id),
            _office.Users,
            _office.Credentials,
            _office.Options,
            _office.Deliveries,
            CancellationToken.None
        );

    [Fact]
    public async Task The_owner_sees_the_latest_link_and_the_allowed_actions()
    {
        var link = _office.SeedLink(_office.Employee, ResetOrigin.Administrative, _office.Owner.Id);

        var result = await GetAsync(_office.Owner, _office.Employee);

        Assert.True(result.IsSuccess);
        var status = result.Value;
        Assert.Equal(CredentialStatus.Active, status.CredentialStatus);
        Assert.True(status.EmailVerified);
        Assert.NotNull(status.LatestResetLink);
        Assert.Equal(link.Id, status.LatestResetLink!.ResetTokenId);
        Assert.Equal(ResetLinkStatus.Pending, status.LatestResetLink.Status);
        Assert.Equal(ResetOrigin.Administrative, status.LatestResetLink.Origin);
        Assert.Equal(ResetLinkDelivery.Unknown, status.LatestResetLink.Delivery);
        Assert.True(status.AllowedActions.SendResetLink);
        Assert.Equal(24, status.AdminLinkLifetimeHours);
    }

    [Fact]
    public async Task An_unverified_email_disables_send_but_still_shows_the_panel()
    {
        var unverified = _office.Add(UserActorType.TenantEmployee, verified: false);

        var result = await GetAsync(_office.Owner, unverified);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AllowedActions.SendResetLink);
        Assert.Null(result.Value.LatestResetLink);
    }

    // AJ-08: la entrega se lee de Notification al abrir el panel; si no responde, Unknown sin romper el GET.
    [Fact]
    public async Task Delivery_is_unknown_when_notification_does_not_answer()
    {
        var eventId = Guid.NewGuid();
        _office.SeedLink(_office.Employee, ResetOrigin.Administrative, _office.Owner.Id, eventId);
        _office.Deliveries.Next = NotificationEmailLookup.Unavailable;

        var result = await GetAsync(_office.Owner, _office.Employee);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResetLinkDelivery.Unknown, result.Value.LatestResetLink!.Delivery);
        Assert.Equal((_office.Tenant.Id, eventId), Assert.Single(_office.Deliveries.Calls));
    }

    [Theory]
    [InlineData("Pending", ResetLinkDelivery.Queued)]
    [InlineData("Sent", ResetLinkDelivery.Sent)]
    [InlineData("Delivered", ResetLinkDelivery.Delivered)]
    [InlineData("Bounced", ResetLinkDelivery.Bounced)]
    [InlineData("Failed", ResetLinkDelivery.Failed)]
    public async Task Delivery_follows_the_notification_log(string status, ResetLinkDelivery expected)
    {
        _office.SeedLink(_office.Employee, ResetOrigin.Administrative, _office.Owner.Id, Guid.NewGuid());
        _office.Deliveries.Next = NotificationEmailLookup.Found(status);

        var result = await GetAsync(_office.Owner, _office.Employee);

        Assert.Equal(expected, result.Value.LatestResetLink!.Delivery);
    }

    [Fact]
    public async Task A_link_without_event_is_not_looked_up()
    {
        _office.SeedLink(_office.Employee, ResetOrigin.Administrative, _office.Owner.Id);

        var result = await GetAsync(_office.Owner, _office.Employee);

        Assert.Equal(ResetLinkDelivery.Unknown, result.Value.LatestResetLink!.Delivery);
        Assert.Empty(_office.Deliveries.Calls);
    }

    [Fact]
    public async Task A_co_admin_is_denied_with_owner_only() =>
        Assert.Equal("Auth.CredentialAdmin.OwnerOnly", (await GetAsync(_office.CoAdmin, _office.Employee)).Error.Code);
}
