using TaxVision.Tenant.Application.Team;
using TaxVision.Tenant.Application.Team.Queries;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

/// <summary>
/// Quién ve la actividad de quién y con qué recorte. La consulta SQL del recorte (ABAC, privadas, facturas,
/// rango) se prueba contra la base en <c>ActivityEntryRepositoryIntegrationTests</c>.
/// </summary>
public sealed class GetMemberActivityHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly ActivityViewerRights NoRights = new(false, false, false, false);

    private readonly ActivityHarness _entries = new();

    private Task<BuildingBlocks.Results.Result<BuildingBlocks.Common.PagedResult<ActivityEntryView>>> Run(
        GetMemberActivityQuery query
    ) => GetMemberActivityHandler.Handle(query, _entries, default);

    private static GetMemberActivityQuery Query(Guid member, ActivityViewerRights rights) =>
        new(TenantId, Viewer, member, rights, null, null, null, null, null);

    [Fact]
    public async Task Anyone_reads_their_own_activity_with_their_own_private_entries()
    {
        var result = await Run(Query(Viewer, NoRights));

        Assert.True(result.IsSuccess);
        var filter = _entries.LastFilter!;
        Assert.Equal(TenantId, filter.TenantId);
        Assert.Equal(Viewer, filter.MemberUserId);
        Assert.True(filter.IncludePrivate);
        Assert.False(filter.IncludeRestricted);
        Assert.Equal(Viewer, filter.AssignedCustomersOf);
    }

    [Fact]
    public async Task Another_member_without_users_work_view_is_forbidden_and_nothing_is_read()
    {
        var result = await Run(Query(Other, NoRights with { CanViewAllCustomers = true, CanViewPrivate = true }));

        Assert.True(result.IsFailure);
        Assert.Equal(TeamErrors.ActivityForbidden, result.Error);
        Assert.Null(_entries.LastFilter);
    }

    [Fact]
    public async Task Another_member_with_users_work_view_hides_their_private_entries()
    {
        var result = await Run(Query(Other, NoRights with { CanViewOthers = true }));

        Assert.True(result.IsSuccess);
        Assert.Equal(Other, _entries.LastFilter!.MemberUserId);
        Assert.False(_entries.LastFilter.IncludePrivate);
    }

    [Fact]
    public async Task Notes_view_all_shows_private_entries_and_invoicing_view_shows_invoices()
    {
        await Run(Query(Other, new ActivityViewerRights(true, false, true, true)));

        Assert.True(_entries.LastFilter!.IncludePrivate);
        Assert.True(_entries.LastFilter.IncludeRestricted);
    }

    [Fact]
    public async Task Customers_view_all_lifts_the_assignment_filter()
    {
        await Run(Query(Other, new ActivityViewerRights(true, true, false, false)));

        Assert.Null(_entries.LastFilter!.AssignedCustomersOf);
    }

    [Fact]
    public async Task Without_customers_view_all_only_the_viewer_assignments_count_even_for_another_member()
    {
        await Run(Query(Other, NoRights with { CanViewOthers = true }));

        Assert.Equal(Viewer, _entries.LastFilter!.AssignedCustomersOf);
    }

    [Theory]
    [InlineData(null, null, 1, 25)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(3, 500, 3, 100)]
    [InlineData(-2, 40, 1, 40)]
    public async Task Paging_defaults_and_caps_the_page_size_at_one_hundred(
        int? page,
        int? size,
        int expectedPage,
        int expectedSize
    )
    {
        await Run(Query(Viewer, NoRights) with { Page = page, Size = size });

        Assert.Equal(expectedPage, _entries.LastFilter!.Page);
        Assert.Equal(expectedSize, _entries.LastFilter.Size);
    }

    [Fact]
    public async Task The_range_is_passed_half_open_and_an_empty_or_inverted_range_is_rejected()
    {
        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        await Run(Query(Viewer, NoRights) with { FromUtc = from, ToUtc = to });
        var empty = await Run(Query(Viewer, NoRights) with { FromUtc = to, ToUtc = to });
        var inverted = await Run(Query(Viewer, NoRights) with { FromUtc = to, ToUtc = from });

        Assert.Equal(from, _entries.LastFilter!.FromUtc);
        Assert.Equal(to, _entries.LastFilter.ToUtc);
        Assert.Equal(TeamErrors.InvalidRange, empty.Error);
        Assert.Equal(TeamErrors.InvalidRange, inverted.Error);
    }

    [Fact]
    public async Task Verbs_are_a_comma_list_of_known_verbs()
    {
        await Run(Query(Viewer, NoRights) with { Verbs = " task.completed, note.created ,," });
        var unknown = await Run(Query(Viewer, NoRights) with { Verbs = "task.completed,note.read" });

        Assert.Equal([ActivityVerbs.TaskCompleted, ActivityVerbs.NoteCreated], _entries.LastFilter!.Verbs);
        Assert.Equal(TeamErrors.UnknownVerbFilter, unknown.Error);
    }

    [Fact]
    public async Task Entries_come_back_with_the_crm_link_of_their_resource()
    {
        var customerId = Guid.NewGuid();
        var note = ActivityEntry
            .Create(
                new ActivityEntryDraft(
                    TenantId,
                    Guid.NewGuid(),
                    Viewer,
                    ActivityVerbs.NoteCreated,
                    Guid.NewGuid(),
                    customerId,
                    ActivityVisibility.Private,
                    DateTime.UtcNow
                )
            )
            .Value;
        _entries.Saved.Add(note);

        var page = (await Run(Query(Viewer, NoRights))).Value;

        var view = Assert.Single(page.Items);
        Assert.Equal(note.Id, view.Id);
        Assert.Equal("Private", view.Visibility);
        Assert.Equal(customerId, view.CustomerId);
        Assert.Equal($"/notes?note={note.ResourceId}", view.Link);
    }
}
