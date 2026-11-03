using System.Security.Claims;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Api;

// Filtros de servidor en los listados y /search: llegan a la consulta y se validan (rango y cliente) con 400.
public sealed class MailListFilterControllerTests
{
    private static readonly DateTime Day = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    private static ClaimsPrincipal Principal() =>
        new(
            new ClaimsIdentity(
                [new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("sub", Guid.NewGuid().ToString())],
                "Test"
            )
        );

    private static T WithUser<T>(T controller)
        where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = Principal() },
        };
        return controller;
    }

    private static ThreadsController Threads(ControllableMessageBus bus) =>
        WithUser(
            new ThreadsController(
                bus,
                new FakeMailboxVisibilityResolver(),
                new FakeCustomerAccessResolver(),
                new FakeCorrelationContext()
            )
        );

    private static SearchController Search(ControllableMessageBus bus) =>
        WithUser(new SearchController(bus, new FakeMailboxVisibilityResolver(), new FakeCustomerAccessResolver()));

    private static DraftsController Drafts(ControllableMessageBus bus) =>
        WithUser(
            new DraftsController(
                bus,
                new FakeDraftRepository(),
                null!,
                null!,
                new FakeCorrelationContext(),
                new FakeMailboxVisibilityResolver(),
                new FakeCustomerAccessResolver()
            )
        );

    private static void AssertBadRequest(IActionResult result, string code) =>
        Assert.Equal(code, Assert.IsType<Error>(Assert.IsType<BadRequestObjectResult>(result).Value).Code);

    [Fact]
    public async Task The_inbox_receives_unlinked_attachments_and_dates()
    {
        var bus = new ControllableMessageBus { NextResult = new PagedResult<InboxThreadSummary>([], 1, 20, 0) };

        await Threads(bus)
            .ListInboxThreads(null, null, false, null, true, true, Day, Day.AddDays(1), 1, 20, CancellationToken.None);

        var filter = Assert.IsType<ListInboxThreadsQuery>(bus.LastInvoked).Filter;
        Assert.True(filter.Unlinked);
        Assert.True(filter.HasAttachmentsOnly);
        Assert.Equal(Day, filter.FromUtc);
        Assert.Equal(Day.AddDays(1), filter.ToUtc);
    }

    [Fact]
    public async Task The_inbox_rejects_a_client_together_with_unlinked_and_a_reversed_range()
    {
        var bus = new ControllableMessageBus();
        var controller = Threads(bus);

        AssertBadRequest(
            await controller.ListInboxThreads(
                null,
                Guid.NewGuid(),
                false,
                null,
                true,
                false,
                null,
                null,
                1,
                20,
                CancellationToken.None
            ),
            "Correspondence.CustomerFilterConflict"
        );
        AssertBadRequest(
            await controller.ListInboxThreads(
                null,
                null,
                false,
                null,
                false,
                false,
                Day,
                Day.AddDays(-1),
                1,
                20,
                CancellationToken.None
            ),
            "Correspondence.InvalidDateRange"
        );
        Assert.Null(bus.LastInvoked);
    }

    [Fact]
    public async Task A_date_without_zone_is_read_as_utc()
    {
        var bus = new ControllableMessageBus { NextResult = new PagedResult<InboxThreadSummary>([], 1, 20, 0) };
        var unspecified = DateTime.SpecifyKind(Day, DateTimeKind.Unspecified);

        await Threads(bus)
            .ListInboxThreads(null, null, false, null, false, false, unspecified, null, 1, 20, CancellationToken.None);

        var from = Assert.IsType<ListInboxThreadsQuery>(bus.LastInvoked).Filter.FromUtc!.Value;
        Assert.Equal(DateTimeKind.Utc, from.Kind);
        Assert.Equal(Day, from);
    }

    [Fact]
    public async Task Folder_listings_receive_attachments_and_dates_and_reject_a_reversed_range()
    {
        var bus = new ControllableMessageBus { NextResult = new PagedResult<SentMessageListItem>([], 1, 20, 0) };
        var controller = Drafts(bus);

        await controller.ListSent(null, false, "all", "maria", true, Day, null, 1, 20, CancellationToken.None);
        var filter = Assert.IsType<ListSentMessagesQuery>(bus.LastInvoked).Filter!;

        Assert.Equal("maria", filter.Search);
        Assert.True(filter.HasAttachmentsOnly);
        Assert.Equal(Day, filter.FromUtc);
        AssertBadRequest(
            await controller.ListTrash(
                null,
                false,
                "all",
                null,
                false,
                Day,
                Day.AddDays(-2),
                1,
                20,
                CancellationToken.None
            ),
            "Correspondence.InvalidDateRange"
        );
    }

    [Fact]
    public async Task Search_receives_every_filter_and_validates_them()
    {
        var bus = new ControllableMessageBus { NextResult = new MailSearchResult([], []) };
        var controller = Search(bus);
        var customerId = Guid.NewGuid();

        await controller.Search("w-2", 5, customerId, false, true, true, Day, Day.AddDays(7), CancellationToken.None);
        var criteria = Assert.IsType<SearchMailQuery>(bus.LastInvoked).Criteria;

        Assert.Equal(customerId, criteria.CustomerId);
        Assert.True(criteria.HasAttachmentsOnly);
        Assert.True(criteria.UnreadOnly);
        Assert.Equal(Day, criteria.FromUtc);
        Assert.Equal(Day.AddDays(7), criteria.ToUtc);
        AssertBadRequest(
            await controller.Search("w-2", 5, customerId, true, false, false, null, null, CancellationToken.None),
            "Correspondence.CustomerFilterConflict"
        );
        AssertBadRequest(
            await controller.Search("w-2", 5, null, false, false, false, Day, Day.AddDays(-1), CancellationToken.None),
            "Correspondence.InvalidDateRange"
        );
    }
}
