using BuildingBlocks.Contracts.Files;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using static TaxVision.CloudStorage.Tests.Application.Library.FakeLibraryReadRepository;

namespace TaxVision.CloudStorage.Tests.Application.Library;

/// <summary>Campos por fila que vienen del dueño: descarga, marca de privacidad y tipo de vista previa.</summary>
public sealed class SearchLibraryRowFieldsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly FakeLibraryReadRepository _repository = new();
    private readonly FakeFileOwnerVisibilityClient _owners = new();

    [Fact]
    public async Task An_owner_without_download_turns_off_preview_and_download_of_that_row()
    {
        var row = Seed(Row(OwnerType.User, FolderType.Tasks, Guid.NewGuid(), "task", "attachment"));
        _owners.Confirm(new VisibleFile(row.Id, "task", Guid.NewGuid(), "Task", null, CanDownload: false));

        var item = Assert.Single((await Read([LibrarySource.Tasks])).Items);

        Assert.False(item.CanDownload);
        Assert.False(item.CanPreview);
    }

    [Fact]
    public async Task The_owner_restriction_reaches_the_row_as_is()
    {
        var row = Seed(Row(OwnerType.User, FolderType.Tasks, Guid.NewGuid(), "task", "attachment"));
        _owners.Confirm(new VisibleFile(row.Id, "task", Guid.NewGuid(), "Task", null, RestrictedTo: "anything_new"));

        var item = Assert.Single((await Read([LibrarySource.Tasks])).Items);

        Assert.Equal("anything_new", item.RestrictedTo);
        Assert.True(item.CanDownload);
        Assert.Equal(LibraryPreviews.Viewer, item.Preview);
    }

    [Fact]
    public async Task A_sql_row_has_no_restriction_and_opens_in_the_viewer()
    {
        Seed(Row(OwnerType.Tenant, FolderType.Documents));

        var item = Assert.Single((await Read([LibrarySource.Documents])).Items);

        Assert.Null(item.RestrictedTo);
        Assert.Equal("viewer", item.Preview);
    }

    [Theory]
    [InlineData("call", "recording", "call_recording", "recording")]
    [InlineData("meeting", "recording", "meeting_recording", "recording")]
    [InlineData("call", "transcript", "call_transcript", "viewer")]
    [InlineData("meeting", "transcript", "meeting_transcript", "viewer")]
    public async Task Only_recordings_open_in_the_recording_player(
        string resourceType,
        string role,
        string kind,
        string preview
    )
    {
        var folder = role == "recording" ? FolderType.Recordings : FolderType.Transcripts;
        var row = Seed(Row(OwnerType.Communication, folder, Guid.NewGuid(), resourceType, role));
        _owners.Confirm(new VisibleFile(row.Id, resourceType, Guid.NewGuid(), "Call with Ana", null));

        var item = Assert.Single((await Read([LibrarySource.Recordings])).Items);

        Assert.Equal(kind, item.Kind);
        Assert.Equal(preview, item.Preview);
    }

    [Fact]
    public async Task A_stage_one_page_has_no_partial_sources()
    {
        Seed(Row(OwnerType.Tenant, FolderType.Documents));

        var page = await Read([LibrarySource.Documents]);

        Assert.Empty(page.PartialSources);
    }

    private LibraryRow Seed(LibraryRow row)
    {
        _repository.Add(Tenant, row);
        return row;
    }

    private Task<LibraryPage> Read(LibrarySource[] sources)
    {
        var access = LibraryAccessPolicyTests.Access(readable: sources, viewAll: true) with
        {
            Variants = new Dictionary<LibrarySource, IReadOnlyList<string>>
            {
                [LibrarySource.Recordings] = ["call", "meeting"],
            },
        };
        return LibraryPageAssembler.ReadAsync(
            new LibraryPageDependencies(
                _repository,
                _owners,
                new LibraryOwnerOptions(),
                new FakeSystemClock(DateTime.UtcNow)
            ),
            Tenant,
            access,
            sources,
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            CancellationToken.None
        );
    }
}
