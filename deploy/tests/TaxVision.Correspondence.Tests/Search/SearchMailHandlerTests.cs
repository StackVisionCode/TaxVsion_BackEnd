using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;

namespace TaxVision.Correspondence.Tests.Search;

public sealed class SearchMailHandlerTests
{
    private sealed class CapturingSearchRepository : IMailSearchRepository
    {
        public MailSearchCriteria? Last { get; private set; }

        public Task<IReadOnlyList<EmailSearchHit>> SearchEmailsAsync(
            MailSearchCriteria criteria,
            CancellationToken ct = default
        )
        {
            Last = criteria;
            return Task.FromResult<IReadOnlyList<EmailSearchHit>>([]);
        }

        public Task<IReadOnlyList<FileSearchHit>> SearchFilesAsync(
            MailSearchCriteria criteria,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FileSearchHit>>([]);
    }

    [Theory]
    [InlineData(0, SearchMailHandler.DefaultLimit)]
    [InlineData(-3, SearchMailHandler.DefaultLimit)]
    [InlineData(7, 7)]
    [InlineData(500, SearchMailHandler.MaxLimit)]
    public async Task The_limit_is_clamped_before_searching(int requested, int expected)
    {
        var repository = new CapturingSearchRepository();

        var result = await SearchMailHandler.Handle(
            new SearchMailQuery(new MailSearchCriteria(Guid.NewGuid(), "w-2", requested, null, null, null)),
            repository,
            CancellationToken.None
        );

        Assert.Equal(expected, repository.Last!.Limit);
        Assert.Empty(result.Emails);
        Assert.Empty(result.Files);
    }
}
