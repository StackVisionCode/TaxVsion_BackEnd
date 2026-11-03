using System.Security.Claims;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Api.Requests;
using TaxVision.Correspondence.Application.Delivery;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Tests.Api;

namespace TaxVision.Correspondence.Tests.Delivery;

public sealed class DeliveryNoticesControllerTests
{
    private static (DeliveryNoticesController Controller, ControllableMessageBus Bus) Build(Guid? restrictTo = null)
    {
        var bus = new ControllableMessageBus();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("sub", Guid.NewGuid().ToString())],
                "Test"
            )
        );
        var controller = new DeliveryNoticesController(
            bus,
            new FakeMailboxVisibilityResolver(),
            new FakeCustomerAccessResolver { RestrictTo = restrictTo }
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, bus);
    }

    [Theory]
    [InlineData("DeliveryNotice.NotFound", 404)]
    [InlineData("DeliveryNotice.OriginalUnavailable", 409)]
    public async Task Service_errors_map_to_their_status(string code, int status)
    {
        var (controller, bus) = Build();
        bus.NextResult = Result.Failure<MessageBodyResult>(new Error(code, "x"));

        var result = await controller.GetOriginal(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(status, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task The_body_is_returned_and_the_gates_reach_the_query()
    {
        var preparer = Guid.NewGuid();
        var (controller, bus) = Build(preparer);
        bus.NextResult = Result.Success(new MessageBodyResult("<p>550</p>", "550", new Dictionary<string, string>()));

        var result = await controller.GetOriginal(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(preparer, Assert.IsType<GetDeliveryNoticeOriginalQuery>(bus.LastInvoked).RestrictToUserId);
    }

    [Fact]
    public async Task Resend_returns_the_new_draft_id_and_passes_recipient_actor_and_gates()
    {
        var preparer = Guid.NewGuid();
        var (controller, bus) = Build(preparer);
        var draftId = Guid.NewGuid();
        bus.NextResult = Result.Success(new ResendDeliveryNoticeResult(draftId));

        var result = await controller.Resend(
            Guid.NewGuid(),
            new ResendDeliveryNoticeBody("fixed@x.com"),
            CancellationToken.None
        );

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(draftId, ok.Value!.GetType().GetProperty("draftId")!.GetValue(ok.Value));
        var command = Assert.IsType<ResendDeliveryNoticeCommand>(bus.LastInvoked);
        Assert.Equal("fixed@x.com", command.Recipient);
        Assert.Equal(preparer, command.RestrictToUserId);
        Assert.NotEqual(Guid.Empty, command.ActorUserId);
    }

    [Fact]
    public async Task Resend_without_body_uses_the_same_address_and_maps_not_found()
    {
        var (controller, bus) = Build();
        bus.NextResult = Result.Failure<ResendDeliveryNoticeResult>(new Error("DeliveryNotice.NotFound", "x"));

        var result = await controller.Resend(Guid.NewGuid(), null, CancellationToken.None);

        Assert.Equal(404, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Null(Assert.IsType<ResendDeliveryNoticeCommand>(bus.LastInvoked).Recipient);
    }
}
