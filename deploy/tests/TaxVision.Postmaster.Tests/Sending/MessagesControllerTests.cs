using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Postmaster.Api.Controllers;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Sending;

public sealed class MessagesControllerTests
{
    // FakeMessageBus no implementa InvokeAsync: si el controller consultara, el test fallaría.
    private static MessagesController CreateController() =>
        new(new FakeMessageBus())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ClaimNames.TenantId, Guid.NewGuid().ToString())], "test")
                    ),
                },
            },
        };

    [Fact]
    public async Task List_answers_400_for_an_unknown_status_without_querying()
    {
        var result = await CreateController().List("Pending", null, null, null, null, 1, 25, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("SentMessageActivityFilter.InvalidStatus", Assert.IsType<Error>(badRequest.Value).Code);
    }

    [Fact]
    public async Task List_answers_400_for_an_unknown_engagement_without_querying()
    {
        var result = await CreateController().List(null, null, "Read", null, null, 1, 25, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("SentMessageActivityFilter.InvalidEngagement", Assert.IsType<Error>(badRequest.Value).Code);
    }
}
