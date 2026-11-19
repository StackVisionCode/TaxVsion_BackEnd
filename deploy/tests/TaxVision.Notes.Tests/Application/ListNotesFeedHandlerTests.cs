using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Tests.Application;

public sealed class ListNotesFeedHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static IOptions<NotesVisibilityOptions> Visibility(bool enabled) =>
        Options.Create(new NotesVisibilityOptions { Enabled = enabled });

    private static ListNotesFeedQuery Query(NoteFeedScope scope, bool hasViewAll, bool canViewAllCustomers = true) =>
        new(TenantId, ActorId, hasViewAll, canViewAllCustomers, new NoteFeedCriteria(Scope: scope), 1, 20);

    [Fact]
    public async Task OthersPrivate_without_view_all_is_Forbidden()
    {
        var repo = new FakeNoteRepository();

        var result = await ListNotesFeedHandler.Handle(
            Query(NoteFeedScope.OthersPrivate, hasViewAll: false),
            repo,
            Visibility(false),
            CancellationToken.None
        );

        Assert.Equal(NoteErrors.Forbidden.Code, result.Error.Code);
        Assert.Null(repo.LastFeed);
    }

    [Fact]
    public async Task OthersPrivate_with_view_all_reaches_the_repository()
    {
        var repo = new FakeNoteRepository();

        var result = await ListNotesFeedHandler.Handle(
            Query(NoteFeedScope.OthersPrivate, hasViewAll: true),
            repo,
            Visibility(false),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteFeedScope.OthersPrivate, repo.LastFeed!.Value.Criteria.Scope);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Assignment_filter_applies_only_with_the_flag_and_without_customers_view_all(
        bool flagEnabled,
        bool canViewAllCustomers,
        bool expectRestricted
    )
    {
        var repo = new FakeNoteRepository();

        await ListNotesFeedHandler.Handle(
            Query(NoteFeedScope.All, hasViewAll: false, canViewAllCustomers),
            repo,
            Visibility(flagEnabled),
            CancellationToken.None
        );

        Assert.Equal(expectRestricted ? ActorId : null, repo.LastFeed!.Value.AssignedToUserId);
    }
}
