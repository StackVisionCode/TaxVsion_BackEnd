using System.Security.Claims;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Tests.Projections;
using Xunit;

namespace TaxVision.Correspondence.Tests.Api;

// Inbox de la oficina: el estado pedido y el acotado por cliente asignado llegan a la consulta.
public sealed class ThreadsControllerInboxTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (ThreadsController Controller, ControllableMessageBus Bus) Build(Guid? restrictTo = null)
    {
        var bus = new ControllableMessageBus { NextResult = new PagedResult<InboxThreadSummary>([], 1, 20, 0) };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("sub", UserId.ToString())],
                "Test"
            )
        );
        var correlation = new FakeCorrelationContext();
        correlation.Set("corr-1");
        var controller = new ThreadsController(
            bus,
            new FakeMailboxVisibilityResolver(),
            new FakeCustomerAccessResolver { RestrictTo = restrictTo },
            correlation
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, bus);
    }

    [Theory]
    [InlineData(null, EmailThreadStatus.Active)]
    [InlineData("Archived", EmailThreadStatus.Archived)]
    [InlineData("all", null)]
    public async Task Status_selects_the_threads_to_list(string? status, EmailThreadStatus? expected)
    {
        var (controller, bus) = Build();

        var result = await controller.ListInboxThreads(status, null, false, null, 1, 20, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<ListInboxThreadsQuery>(bus.LastInvoked);
        Assert.Equal(expected, query.Filter.Status);
    }

    [Fact]
    public async Task An_unknown_status_is_rejected()
    {
        var (controller, bus) = Build();

        var result = await controller.ListInboxThreads("Deleted", null, false, null, 1, 20, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Correspondence.InvalidThreadStatus", Assert.IsType<Error>(bad.Value).Code);
        Assert.Null(bus.LastInvoked);
    }

    [Fact]
    public async Task The_customer_restriction_reaches_the_query()
    {
        var preparer = Guid.NewGuid();
        var (controller, bus) = Build(preparer);

        await controller.ListInboxThreads(null, null, true, "  w-2  ", 1, 20, CancellationToken.None);

        var query = Assert.IsType<ListInboxThreadsQuery>(bus.LastInvoked);
        Assert.Equal(preparer, query.Filter.RestrictToUserId);
        Assert.True(query.Filter.UnreadOnly);
        Assert.Equal("w-2", query.Filter.Search);
    }

    [Fact]
    public async Task Archive_passes_the_customer_restriction_the_actor_and_the_correlation()
    {
        var preparer = Guid.NewGuid();
        var (controller, bus) = Build(preparer);
        bus.NextResult = Result.Success();

        var result = await controller.Archive(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var command = Assert.IsType<ArchiveThreadCommand>(bus.LastInvoked);
        Assert.Equal(preparer, command.RestrictToUserId);
        Assert.Equal(UserId, command.ActorUserId);
        Assert.Equal("corr-1", command.CorrelationId);
    }
}
