using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Files;

namespace TaxVision.Correspondence.Tests.Work;

// Visibilidad por archivo para la biblioteca: el handler pasa al lector la misma visibilidad que Mail.
public sealed class GetVisibleMailFilesHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly RecordingReader _reader = new();

    private Task<FileVisibilityResponse> HandleAsync(
        IReadOnlyList<Guid> fileIds,
        IReadOnlyCollection<Guid>? visible,
        Guid? restrictTo
    ) =>
        GetVisibleMailFilesHandler.Handle(
            new GetVisibleMailFilesQuery(Tenant, fileIds, visible, restrictTo),
            _reader,
            CancellationToken.None
        );

    [Fact]
    public async Task Passes_the_viewer_mailboxes_and_customer_restriction_to_the_reader()
    {
        var file = Guid.NewGuid();
        var mailbox = Guid.NewGuid();
        var restrictTo = Guid.NewGuid();
        var visibleFile = new VisibleFile(file, WorkResourceTypes.EmailMessage, Guid.NewGuid(), "Hello", null);
        _reader.Result = [visibleFile];

        var response = await HandleAsync([file, file], [mailbox], restrictTo);

        Assert.Equal([visibleFile], response.Items);
        var call = Assert.Single(_reader.Calls);
        Assert.Equal(Tenant, call.TenantId);
        Assert.Equal([file], call.FileIds);
        Assert.Equal([mailbox], call.Visible);
        Assert.Equal(restrictTo, call.RestrictTo);
    }

    [Fact]
    public async Task Without_files_the_reader_is_not_queried()
    {
        var response = await HandleAsync([], null, null);

        Assert.Empty(response.Items);
        Assert.Empty(_reader.Calls);
    }

    [Fact]
    public async Task Without_visible_mailboxes_the_reader_is_not_queried()
    {
        var response = await HandleAsync([Guid.NewGuid()], [], null);

        Assert.Empty(response.Items);
        Assert.Empty(_reader.Calls);
    }

    private sealed class RecordingReader : IMailFileVisibilityReader
    {
        public List<(
            Guid TenantId,
            IReadOnlyCollection<Guid> FileIds,
            IReadOnlyCollection<Guid>? Visible,
            Guid? RestrictTo
        )> Calls { get; } = [];

        public IReadOnlyList<VisibleFile> Result { get; set; } = [];

        public Task<IReadOnlyList<VisibleFile>> GetVisibleFilesAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> fileIds,
            IReadOnlyCollection<Guid>? visibleAccountIds,
            Guid? restrictToUserId,
            Guid viewerUserId,
            bool canDownloadIncoming,
            CancellationToken ct = default
        )
        {
            Calls.Add((tenantId, fileIds, visibleAccountIds, restrictToUserId));
            return Task.FromResult(Result);
        }
    }
}
