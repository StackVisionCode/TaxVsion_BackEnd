using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Connectors.Api.Controllers;
using TaxVision.Connectors.Api.Requests;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Api;

public sealed class InternalMailboxResyncControllerTests
{
    private static readonly DateTime ToUtc = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static ControllerContext ServiceTokenFor(Guid tenantId) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new Claim(ClaimNames.ActorType, nameof(ActorType.Service)),
                            new Claim(ClaimNames.TenantId, tenantId.ToString()),
                        ],
                        authenticationType: "Test"
                    )
                ),
            },
        };

    [Fact]
    public async Task Resync_is_forbidden_when_the_body_tenant_is_not_the_token_tenant()
    {
        var bus = new FakeMessageBus();
        var controller = new InternalMailboxResyncController(bus)
        {
            ControllerContext = ServiceTokenFor(Guid.NewGuid()),
        };

        var result = await controller.Resync(
            Guid.NewGuid(),
            new MailboxResyncRequest(Guid.NewGuid(), ToUtc.AddDays(-1), ToUtc),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Resync_starts_the_resync_for_the_token_tenant()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var bus = new FakeMessageBus();
        var controller = new InternalMailboxResyncController(bus) { ControllerContext = ServiceTokenFor(tenantId) };

        var result = await controller.Resync(
            accountId,
            new MailboxResyncRequest(tenantId, ToUtc.AddDays(-1), ToUtc),
            CancellationToken.None
        );

        Assert.IsType<AcceptedResult>(result);
        var command = Assert.IsType<StartMailboxResyncCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(new StartMailboxResyncCommand(tenantId, accountId, ToUtc.AddDays(-1), ToUtc), command);
    }

    [Fact]
    public async Task Resync_of_an_account_that_is_not_active_is_a_conflict()
    {
        var tenantId = Guid.NewGuid();
        var bus = new FakeMessageBus
        {
            InvokeResult = Result.Failure(new Error(StartMailboxResyncHandler.AccountNotActiveCode, "Not active.")),
        };
        var controller = new InternalMailboxResyncController(bus) { ControllerContext = ServiceTokenFor(tenantId) };

        var result = await controller.Resync(
            Guid.NewGuid(),
            new MailboxResyncRequest(tenantId, ToUtc.AddDays(-1), ToUtc),
            CancellationToken.None
        );

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }
}
