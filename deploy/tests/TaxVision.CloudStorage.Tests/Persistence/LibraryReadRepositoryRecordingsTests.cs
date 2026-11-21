using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Integration;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>
/// Rama SQL de <c>recordings</c> contra SQL Server real: grabaciones y <c>.txt</c> de las llamadas y reuniones del
/// ámbito, cada tipo con su variante. <c>transcript_data</c> y las pistas nunca son candidatos.
/// </summary>
public sealed class LibraryReadRepositoryRecordingsTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _call = Guid.NewGuid();
    private readonly Guid _meeting = Guid.NewGuid();
    private readonly Guid _otherCall = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<string, Guid> _ids = [];
    private int _minute;

    public async Task InitializeAsync()
    {
        var files = new List<FileObject>
        {
            Declared(File("callRecording", FolderType.Recordings), "call", _call, "recording"),
            Declared(File("callTranscript", FolderType.Transcripts), "call", _call, "transcript"),
            Declared(File("callTranscriptData", FolderType.Transcripts), "call", _call, "transcript_data"),
            Declared(File("meetingRecording", FolderType.Recordings), "meeting", _meeting, "recording"),
            Declared(File("meetingTranscript", FolderType.Transcripts), "meeting", _meeting, "transcript"),
            Declared(File("otherCallRecording", FolderType.Recordings), "call", _otherCall, "recording"),
            File("track", FolderType.RecordingTracks),
            File("undeclaredRecording", FolderType.Recordings),
        };
        await using var db = NewContext();
        db.Files.AddRange(files);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Only_recordings_and_transcripts_of_the_scope_are_candidates()
    {
        var rows = await Search(["call", "meeting"], new CommunicationFileScope([], [_call], [_meeting], []));

        Assert.Equal(
            Names("callRecording", "callTranscript", "meetingRecording", "meetingTranscript"),
            rows.Select(row => row.Id).ToHashSet()
        );
        Assert.All(rows, row => Assert.Equal(LibrarySource.Recordings, LibrarySourceCatalog.Classify(row)));
    }

    [Fact]
    public async Task Each_type_needs_its_variant()
    {
        var scope = new CommunicationFileScope([], [_call, _otherCall], [_meeting], []);

        var callsOnly = await Search(["call"], scope);
        var meetingsOnly = await Search(["meeting"], scope);

        Assert.Equal(
            Names("callRecording", "callTranscript", "otherCallRecording"),
            callsOnly.Select(row => row.Id).ToHashSet()
        );
        Assert.Equal(Names("meetingRecording", "meetingTranscript"), meetingsOnly.Select(row => row.Id).ToHashSet());
    }

    [Fact]
    public async Task A_meeting_id_in_the_call_list_does_not_open_it()
    {
        var rows = await Search(["call", "meeting"], new CommunicationFileScope([], [_meeting], [_call], []));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Without_a_scope_the_download_lookup_still_finds_the_candidate()
    {
        var access = Access(["call", "meeting"]);
        await using var db = NewContext();

        var row = await new LibraryReadRepository(db).FindAsync(
            _tenant,
            LibraryAccessPolicy.ScopeFor(access, access.Readable),
            _ids["otherCallRecording"],
            CancellationToken.None
        );

        Assert.NotNull(row);
        Assert.Null(
            await new LibraryReadRepository(db).FindAsync(
                _tenant,
                LibraryAccessPolicy.ScopeFor(access, access.Readable),
                _ids["callTranscriptData"],
                CancellationToken.None
            )
        );
    }

    private async Task<IReadOnlyList<LibraryRow>> Search(string[] variants, CommunicationFileScope scope)
    {
        await using var db = NewContext();
        return await new LibraryReadRepository(db).SearchAsync(
            _tenant,
            LibraryAccessPolicy.ScopeFor(Access(variants), [LibrarySource.Recordings], scope),
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            100,
            CancellationToken.None
        );
    }

    private static LibraryAccess Access(string[] variants) =>
        new([LibrarySource.Recordings], true, false, true, [])
        {
            Variants = new Dictionary<LibrarySource, IReadOnlyList<string>> { [LibrarySource.Recordings] = variants },
        };

    private HashSet<Guid> Names(params string[] names) => names.Select(name => _ids[name]).ToHashSet();

    private FileObject File(string label, FolderType folder)
    {
        var id = Guid.NewGuid();
        _ids[label] = id;
        var createdAtUtc = _now.AddMinutes(-++_minute);
        var key = ObjectKey.Create($"tenants/{_tenant:N}/communication/files/2026/{id:N}.webm").Value;
        var file = FileObject
            .Register(
                id,
                _tenant,
                OwnerType.Communication,
                Guid.NewGuid(),
                folder,
                2026,
                key,
                $"{label}.webm",
                "video/webm",
                10,
                Guid.NewGuid(),
                createdAtUtc,
                createdAtUtc.AddHours(1)
            )
            .Value;
        file.MarkPendingScan(createdAtUtc);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "video/webm", createdAtUtc);
        return file;
    }

    private static FileObject Declared(FileObject file, string resourceType, Guid resourceId, string role)
    {
        file.DeclareSource(
            FileSourceDeclaration.Create("communication", resourceType, resourceId, role, null).Value,
            DateTime.UtcNow
        );
        return file;
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>()
                .UseSqlServer(CloudStorageIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenant)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
