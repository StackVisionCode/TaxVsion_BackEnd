using System.Security.Claims;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Tests.Api;

namespace TaxVision.Correspondence.Tests.Search;

public sealed class SearchControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (SearchController Controller, ControllableMessageBus Bus) Build(
        IReadOnlyCollection<Guid>? visible = null,
        Guid? restrictTo = null
    )
    {
        var bus = new ControllableMessageBus { NextResult = new MailSearchResult([], [], []) };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("sub", UserId.ToString())],
                "Test"
            )
        );
        var controller = new SearchController(
            bus,
            new FakeMailboxVisibilityResolver { Visible = visible },
            new FakeCustomerAccessResolver { RestrictTo = restrictTo }
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, bus);
    }

    private static Task<IActionResult> Call(
        SearchController controller,
        string? q,
        int limit = 0,
        string? from = null,
        string? to = null,
        string? subject = null,
        string? folder = null,
        string? groups = null
    ) =>
        controller.Search(
            q,
            limit,
            null,
            false,
            false,
            false,
            null,
            null,
            from,
            to,
            subject,
            folder,
            groups,
            CancellationToken.None
        );

    private static void AssertBadRequest(IActionResult result, string code)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(code, Assert.IsType<Error>(bad.Value).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Without_any_text_criterion_the_search_is_rejected(string? q)
    {
        var (controller, bus) = Build();

        AssertBadRequest(await Call(controller, q), "Correspondence.SearchCriteriaRequired");
        Assert.Null(bus.LastInvoked);
    }

    [Theory]
    [InlineData(" a ", null, null, null)]
    [InlineData(null, "a", null, null)]
    [InlineData(null, null, " b", null)]
    [InlineData("invoice", null, null, "c")]
    public async Task Any_text_shorter_than_two_letters_is_rejected(
        string? q,
        string? from,
        string? to,
        string? subject
    )
    {
        var (controller, bus) = Build();

        AssertBadRequest(
            await Call(controller, q, from: from, to: to, subject: subject),
            "Correspondence.SearchTermTooShort"
        );
        Assert.Null(bus.LastInvoked);
    }

    [Fact]
    public async Task A_field_filter_alone_is_enough_and_every_text_is_trimmed_and_capped()
    {
        var (controller, bus) = Build();

        Assert.IsType<OkObjectResult>(
            await Call(controller, null, from: "  ana@x.com ", to: new string('t', 250), subject: " W-2 ")
        );

        var criteria = Assert.IsType<SearchMailQuery>(bus.LastInvoked).Criteria;
        Assert.Null(criteria.Term);
        Assert.Equal("ana@x.com", criteria.From);
        Assert.Equal(200, criteria.To!.Length);
        Assert.Equal("W-2", criteria.Subject);
        Assert.Null(criteria.Folder);
        Assert.Equal(MailSearchGroups.All, criteria.Groups);
    }

    [Theory]
    [InlineData("sent", MailSearchFolder.Sent)]
    [InlineData("Inbox", MailSearchFolder.Inbox)]
    [InlineData("ARCHIVED", MailSearchFolder.Archived)]
    [InlineData("drafts", MailSearchFolder.Drafts)]
    [InlineData("trash", MailSearchFolder.Trash)]
    public async Task The_folder_is_parsed_without_case(string folder, MailSearchFolder expected)
    {
        var (controller, bus) = Build();

        await Call(controller, "invoice", folder: folder);

        Assert.Equal(expected, Assert.IsType<SearchMailQuery>(bus.LastInvoked).Criteria.Folder);
    }

    [Theory]
    [InlineData("junk")]
    [InlineData("1")]
    public async Task An_unknown_folder_is_rejected(string folder)
    {
        var (controller, bus) = Build();

        AssertBadRequest(await Call(controller, "invoice", folder: folder), "Correspondence.InvalidSearchFolder");
        Assert.Null(bus.LastInvoked);
    }

    [Theory]
    [InlineData("people", MailSearchGroups.People)]
    [InlineData(" Emails , files ", MailSearchGroups.Emails | MailSearchGroups.Files)]
    [InlineData("emails,files,people", MailSearchGroups.All)]
    [InlineData("", MailSearchGroups.All)]
    [InlineData(" , ", MailSearchGroups.All)]
    public async Task Groups_are_a_comma_separated_list_and_default_to_all(string groups, MailSearchGroups expected)
    {
        var (controller, bus) = Build();

        await Call(controller, "invoice", groups: groups);

        Assert.Equal(expected, Assert.IsType<SearchMailQuery>(bus.LastInvoked).Criteria.Groups);
    }

    [Theory]
    [InlineData("events")]
    [InlineData("emails,contacts")]
    public async Task An_unknown_group_is_rejected(string groups)
    {
        var (controller, bus) = Build();

        AssertBadRequest(await Call(controller, "invoice", groups: groups), "Correspondence.InvalidSearchGroups");
        Assert.Null(bus.LastInvoked);
    }

    [Fact]
    public async Task The_term_is_trimmed_and_capped_and_the_gates_reach_the_query()
    {
        var preparer = Guid.NewGuid();
        var (controller, bus) = Build(visible: [Guid.NewGuid()], restrictTo: preparer);

        var result = await Call(controller, "  " + new string('x', 250) + "  ", limit: 5);

        Assert.IsType<OkObjectResult>(result);
        var criteria = Assert.IsType<SearchMailQuery>(bus.LastInvoked).Criteria;
        Assert.Equal(200, criteria.Term!.Length);
        Assert.Equal(5, criteria.Limit);
        Assert.Equal(preparer, criteria.RestrictToUserId);
        Assert.NotNull(criteria.VisibleAccountIds);
        // Sin buzón de oficina: solo sus propios borradores.
        Assert.Equal(UserId, criteria.DraftOwnerUserId);
    }

    [Fact]
    public async Task An_office_reader_searches_every_draft()
    {
        var (controller, bus) = Build(visible: null);

        await Call(controller, "invoice");

        var criteria = Assert.IsType<SearchMailQuery>(bus.LastInvoked).Criteria;
        Assert.Null(criteria.DraftOwnerUserId);
        Assert.Null(criteria.VisibleAccountIds);
    }
}
