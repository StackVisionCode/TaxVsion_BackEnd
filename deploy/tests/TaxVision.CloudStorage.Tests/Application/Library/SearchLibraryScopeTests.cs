using BuildingBlocks.Contracts.Files;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using static TaxVision.CloudStorage.Tests.Application.Library.FakeLibraryReadRepository;

namespace TaxVision.CloudStorage.Tests.Application.Library;

/// <summary>
/// Paso 0 de la búsqueda: el ámbito de Communication. Se prueba con las definiciones de chat y grabaciones, que
/// existen en el catálogo pero todavía no se ofrecen; el acceso se arma a mano.
/// </summary>
public sealed class SearchLibraryScopeTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeLibraryReadRepository _repository = new();
    private readonly FakeFileOwnerVisibilityClient _owners = new();
    private readonly SteppingClock _clock = new(Now);
    private int _minute;

    [Fact]
    public async Task The_scope_is_asked_once_per_request_even_over_several_tranches()
    {
        for (var i = 0; i < 5; i++)
            Chat();
        var scope = new CommunicationFileScope([Guid.NewGuid()], [], [], []);
        _owners.Scope = new OwnerScope(true, scope);

        await Read([LibrarySource.Chat], take: 5, window: 2);

        Assert.Equal(1, _owners.ScopeCalls);
        Assert.Equal(3, _repository.Calls);
        Assert.All(_repository.RequestedScopes, requested => Assert.Same(scope, requested.Communication));
    }

    [Theory]
    [InlineData(LibrarySource.Documents)]
    [InlineData(LibrarySource.Tasks)]
    public async Task Without_chat_or_recordings_the_scope_is_not_asked(LibrarySource source)
    {
        Doc();

        await Read([source]);

        Assert.Equal(0, _owners.ScopeCalls);
        Assert.Null(Assert.Single(_repository.RequestedScopes).Communication);
    }

    [Fact]
    public async Task A_failed_scope_leaves_chat_and_recordings_out_of_the_sql_and_unavailable()
    {
        var doc = Doc();
        Chat();
        _owners.Scope = OwnerScope.Unavailable;

        var page = await Read([LibrarySource.Documents, LibrarySource.Chat, LibrarySource.Recordings]);

        Assert.Equal([doc.Id], page.Items.Select(item => item.Id));
        Assert.Equal(["chat", "recordings"], page.UnavailableSources);
        Assert.All(
            _repository.RequestedScopes,
            requested => Assert.Equal([LibrarySource.Documents], requested.Sources.Select(s => s.Source))
        );
        Assert.Empty(_owners.Calls);
    }

    [Fact]
    public async Task With_only_scoped_sources_and_a_failed_scope_the_page_ends()
    {
        Chat();
        _owners.Scope = OwnerScope.Unavailable;

        var page = await Read([LibrarySource.Chat]);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.Equal(0, _repository.Calls);
        Assert.Equal(["chat"], page.UnavailableSources);
    }

    [Fact]
    public async Task A_truncated_scope_list_marks_its_source_as_partial()
    {
        _owners.Scope = new OwnerScope(
            true,
            new CommunicationFileScope([], [], [], [CommunicationFileScopeLists.Meetings])
        );

        var page = await Read([LibrarySource.Chat, LibrarySource.Recordings]);

        Assert.Equal(["recordings"], page.PartialSources);
        Assert.Empty(page.UnavailableSources);
    }

    [Fact]
    public async Task The_scope_time_counts_in_the_owner_budget()
    {
        for (var i = 0; i < 10; i++)
            Chat();
        _owners.OnScope = () => _clock.Advance(TimeSpan.FromSeconds(5));
        _owners.OnCall = () => _clock.Advance(TimeSpan.FromSeconds(2));

        var page = await Read([LibrarySource.Chat], take: 5, window: 2);

        // Sin el ámbito habría tres tramos (2 + 2 + 2 s); con él, 5 + 2 s agotan los 6 s tras el primero.
        Assert.Equal(1, _repository.Calls);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task Confirmed_chat_rows_are_listed_with_the_owner_fields()
    {
        var row = Chat();
        _owners.Confirm(
            new VisibleFile(
                row.Id,
                FileVisibilityResourceTypes.Conversation,
                Guid.NewGuid(),
                "Smith family",
                null,
                RestrictedTo: FileVisibilityRestrictions.Participants
            )
        );

        var item = Assert.Single((await Read([LibrarySource.Chat])).Items);

        Assert.Equal("chat", item.Source);
        Assert.Equal("chat_attachment", item.Kind);
        Assert.Equal("participants", item.RestrictedTo);
        Assert.Equal(FileOwnerService.Communication, Assert.Single(_owners.Calls).Owner);
    }

    private LibraryRow Doc() => Seed(Row(OwnerType.Tenant, FolderType.Documents, createdAtUtc: Next()));

    private LibraryRow Chat() =>
        Seed(
            Row(
                OwnerType.Communication,
                FolderType.Other,
                Guid.NewGuid(),
                "conversation",
                "attachment",
                createdAtUtc: Next()
            )
        );

    private DateTime Next() => Now.AddMinutes(-++_minute);

    private LibraryRow Seed(LibraryRow row)
    {
        _repository.Add(Tenant, row);
        return row;
    }

    private Task<LibraryPage> Read(LibrarySource[] sources, int take = 50, int window = 100)
    {
        var access = LibraryAccessPolicyTests.Access(readable: sources, viewAll: true) with
        {
            Variants = new Dictionary<LibrarySource, IReadOnlyList<string>>
            {
                [LibrarySource.Recordings] = ["call", "meeting"],
            },
        };
        return LibraryPageAssembler.ReadAsync(
            new LibraryPageDependencies(_repository, _owners, new LibraryOwnerOptions { Window = window }, _clock),
            Tenant,
            access,
            sources,
            LibraryFilter.Create(new LibrarySearchRequest { Take = take }).Value,
            CancellationToken.None
        );
    }
}
