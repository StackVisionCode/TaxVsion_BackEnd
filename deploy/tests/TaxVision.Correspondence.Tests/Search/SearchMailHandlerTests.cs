using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Tests.Messages;

namespace TaxVision.Correspondence.Tests.Search;

public sealed class SearchMailHandlerTests
{
    private sealed class CapturingSearchRepository : IMailSearchRepository
    {
        public MailSearchCriteria? Last { get; private set; }
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<EmailSearchHit>> SearchEmailsAsync(
            MailSearchCriteria criteria,
            CancellationToken ct = default
        )
        {
            Last = criteria;
            Calls.Add("emails");
            return Task.FromResult<IReadOnlyList<EmailSearchHit>>([]);
        }

        public Task<IReadOnlyList<FileSearchHit>> SearchFilesAsync(
            MailSearchCriteria criteria,
            CancellationToken ct = default
        )
        {
            Last = criteria;
            Calls.Add("files");
            return Task.FromResult<IReadOnlyList<FileSearchHit>>([]);
        }

        public Task<IReadOnlyList<PersonSearchHit>> SearchPeopleAsync(
            MailSearchCriteria criteria,
            CancellationToken ct = default
        )
        {
            Last = criteria;
            Calls.Add("people");
            return Task.FromResult<IReadOnlyList<PersonSearchHit>>([]);
        }
    }

    private static Task<MailSearchResult> Handle(
        MailSearchCriteria criteria,
        IMailSearchRepository repository,
        FakeConnectorsClient? connectors = null
    ) =>
        SearchMailHandler.Handle(
            new SearchMailQuery(criteria),
            repository,
            connectors ?? new FakeConnectorsClient(),
            NullLogger<SearchMailQuery>.Instance,
            CancellationToken.None
        );

    private static MailSearchCriteria Criteria(
        string? term = "w-2",
        int limit = 10,
        string? from = null,
        MailSearchGroups groups = MailSearchGroups.All
    ) => new(Guid.NewGuid(), term, limit, null, null, null, From: from, Groups: groups);

    [Theory]
    [InlineData(0, SearchMailHandler.DefaultLimit)]
    [InlineData(-3, SearchMailHandler.DefaultLimit)]
    [InlineData(7, 7)]
    [InlineData(500, SearchMailHandler.MaxLimit)]
    public async Task The_limit_is_clamped_before_searching(int requested, int expected)
    {
        var repository = new CapturingSearchRepository();

        var result = await Handle(Criteria(limit: requested), repository);

        Assert.Equal(expected, repository.Last!.Limit);
        Assert.Empty(result.Emails);
        Assert.Empty(result.Files);
        Assert.Empty(result.People);
    }

    [Fact]
    public async Task Only_the_requested_groups_are_computed()
    {
        var repository = new CapturingSearchRepository();
        var connectors = new FakeConnectorsClient();

        await Handle(Criteria(groups: MailSearchGroups.Emails | MailSearchGroups.Files), repository, connectors);

        Assert.Equal(["emails", "files"], repository.Calls);
        // Sin personas ni from=: no hace falta preguntar los buzones.
        Assert.Equal(0, connectors.MailboxAddressesCalls);
    }

    [Fact]
    public async Task From_resolves_the_sending_mailboxes_and_people_get_the_own_addresses()
    {
        var office = Guid.NewGuid();
        var personal = Guid.NewGuid();
        var repository = new CapturingSearchRepository();
        var connectors = new FakeConnectorsClient
        {
            MailboxAddressesResponse = Result.Success<IReadOnlyList<MailboxAddress>>([
                new(office, "Office@Firm.com", "Smith Tax Office"),
                new(personal, "joe@firm.com", "Joe"),
            ]),
        };

        await Handle(Criteria(term: null, from: "office"), repository, connectors);

        var criteria = repository.Last!;
        Assert.Equal([office], criteria.SenderAccountIds);
        Assert.Equal(["office@firm.com", "joe@firm.com"], criteria.OwnMailboxAddresses);
        Assert.Equal(1, connectors.MailboxAddressesCalls);
    }

    [Fact]
    public async Task When_connectors_fails_the_search_still_runs_without_mailboxes()
    {
        var repository = new CapturingSearchRepository();
        var connectors = new FakeConnectorsClient
        {
            MailboxAddressesResponse = Result.Failure<IReadOnlyList<MailboxAddress>>(
                new Error("ConnectorsClient.Unavailable", "down")
            ),
        };

        await Handle(Criteria(from: "office"), repository, connectors);

        Assert.Equal(["emails", "files", "people"], repository.Calls);
        Assert.Empty(repository.Last!.SenderAccountIds!);
        Assert.Empty(repository.Last!.OwnMailboxAddresses!);
    }
}
