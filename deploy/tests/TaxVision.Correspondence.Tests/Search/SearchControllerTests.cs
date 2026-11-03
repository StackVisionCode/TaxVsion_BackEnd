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
        var bus = new ControllableMessageBus { NextResult = new MailSearchResult([], []) };
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

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData(" a ")]
    public async Task A_missing_or_one_letter_term_is_rejected(string? q)
    {
        var (controller, bus) = Build();

        var result = await controller.Search(q, 0, null, false, false, false, null, null, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Correspondence.SearchTermTooShort", Assert.IsType<Error>(bad.Value).Code);
        Assert.Null(bus.LastInvoked);
    }

    [Fact]
    public async Task The_term_is_trimmed_and_capped_and_the_gates_reach_the_query()
    {
        var preparer = Guid.NewGuid();
        var (controller, bus) = Build(visible: [Guid.NewGuid()], restrictTo: preparer);

        var result = await controller.Search(
            "  " + new string('x', 250) + "  ",
            5,
            null,
            false,
            false,
            false,
            null,
            null,
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result);
        var criteria = Assert.IsType<SearchMailQuery>(bus.LastInvoked).Criteria;
        Assert.Equal(200, criteria.Term.Length);
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

        await controller.Search("invoice", 0, null, false, false, false, null, null, CancellationToken.None);

        var criteria = Assert.IsType<SearchMailQuery>(bus.LastInvoked).Criteria;
        Assert.Null(criteria.DraftOwnerUserId);
        Assert.Null(criteria.VisibleAccountIds);
    }
}
