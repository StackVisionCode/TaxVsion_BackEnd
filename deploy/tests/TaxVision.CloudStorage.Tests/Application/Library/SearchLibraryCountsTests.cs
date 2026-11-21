using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Application.Library.Queries;
using TaxVision.CloudStorage.Domain.Files;
using static TaxVision.CloudStorage.Tests.Application.Library.FakeLibraryReadRepository;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class SearchLibraryCountsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly FakeLibraryReadRepository _repository = new();
    private readonly FakeFileOwnerVisibilityClient _owners = new();

    [Fact]
    public async Task Row_scoped_sources_have_no_count_and_no_total()
    {
        _repository
            .Add(Tenant, Row(OwnerType.Tenant, FolderType.Documents))
            .Add(Tenant, Row(OwnerType.User, FolderType.Tasks, Guid.NewGuid(), "task", "attachment"));
        var access = LibraryAccessPolicyTests.Access(readable: [LibrarySource.Documents, LibrarySource.Tasks]);

        var response = await Search(access, new LibrarySearchRequest { Source = ["documents"] });

        Assert.Equal(
            [new LibrarySourceCount("documents", 1), new LibrarySourceCount("tasks", null)],
            response.Counts!.BySource
        );
        Assert.Null(response.Counts.Total);
        // Contar no pregunta al dueño, y la página solo tocó documents.
        Assert.Empty(_owners.Calls);
    }

    [Fact]
    public async Task Only_sql_sources_are_counted_in_the_repository()
    {
        var access = LibraryAccessPolicyTests.Access(readable: [LibrarySource.Documents, LibrarySource.Mail]);

        await Search(access, new LibrarySearchRequest { Source = ["documents"] });

        Assert.All(
            _repository.RequestedScopes,
            scope => Assert.Equal([LibrarySource.Documents], scope.Sources.Select(s => s.Source))
        );
    }

    [Fact]
    public async Task Without_row_scoped_sources_the_total_is_exact()
    {
        _repository.Add(Tenant, Row(OwnerType.Tenant, FolderType.Documents));
        var access = LibraryAccessPolicyTests.Access(readable: [LibrarySource.Documents, LibrarySource.Billing]);

        var response = await Search(access);

        Assert.Equal(1, response.Counts!.Total);
    }

    [Fact]
    public async Task With_only_row_scoped_sources_the_repository_counts_nothing()
    {
        var access = LibraryAccessPolicyTests.Access(readable: [LibrarySource.Forms]);

        var response = await Search(access, new LibrarySearchRequest { Source = ["forms"] });

        Assert.Equal([new LibrarySourceCount("forms", null)], response.Counts!.BySource);
        Assert.Null(response.Counts.Total);
        Assert.Equal(1, _repository.Calls);
    }

    [Fact]
    public async Task The_unavailable_sources_reach_the_response()
    {
        _repository.Add(Tenant, Row(OwnerType.User, FolderType.Tasks, Guid.NewGuid(), "task", "attachment"));
        _owners.Down(FileOwnerService.Tasks);
        var access = LibraryAccessPolicyTests.Access(readable: [LibrarySource.Tasks, LibrarySource.Forms]);

        var response = await Search(access);

        Assert.Empty(response.Items);
        Assert.Equal(["tasks", "forms"], response.UnavailableSources);
    }

    private Task<LibrarySearchResponse> Search(LibraryAccess access, LibrarySearchRequest? request = null) =>
        SearchLibraryHandler.Handle(
            new SearchLibraryQuery(Tenant, access, LibraryFilter.Create(request ?? new LibrarySearchRequest()).Value),
            _repository,
            _owners,
            Options.Create(new LibraryOwnerOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );
}
