using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Application.Library.Queries;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class IssueLibraryDownloadUrlRowSourceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly FakeFileObjectRepository _files = new();
    private readonly FakeLibraryReadRepository _library = new();
    private readonly FakeFileOwnerVisibilityClient _owners = new();
    private readonly List<IssueDownloadUrlQuery> _delegated = [];

    [Fact]
    public async Task A_confirmed_task_attachment_gets_a_url()
    {
        var file = SeedTaskAttachment();
        _owners.Confirm(file.Id);

        var result = await Issue(file);

        Assert.True(result.IsSuccess);
        Assert.Equal(file.Id, Assert.Single(_delegated).FileId);
    }

    [Fact]
    public async Task An_unconfirmed_task_attachment_is_not_found()
    {
        var file = SeedTaskAttachment();

        var result = await Issue(file);

        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Empty(_delegated);
    }

    [Fact]
    public async Task An_unavailable_owner_is_a_source_unavailable_error()
    {
        var file = SeedTaskAttachment();
        _owners.Confirm(file.Id).Down(FileOwnerService.Tasks);

        var result = await Issue(file);

        Assert.Equal(LibraryErrors.SourceUnavailable, result.Error);
        Assert.Empty(_delegated);
    }

    [Fact]
    public async Task The_owner_is_asked_for_that_file_with_the_download_purpose()
    {
        var file = SeedTaskAttachment();
        _owners.Confirm(file.Id);

        await Issue(file);

        var call = Assert.Single(_owners.Calls);
        Assert.Equal(FileOwnerService.Tasks, call.Owner);
        Assert.Equal([file.Id], call.FileIds);
        Assert.Equal(FileVisibilityPurposes.Download, call.Purpose);
    }

    [Fact]
    public async Task A_sql_source_file_does_not_call_any_owner()
    {
        var file = Seed(OwnerType.Tenant, FolderType.Documents, null, null, null);

        var result = await Issue(file);

        Assert.True(result.IsSuccess);
        Assert.Empty(_owners.Calls);
    }

    private FileObject SeedTaskAttachment() =>
        Seed(OwnerType.User, FolderType.Tasks, Guid.NewGuid(), "task", "attachment");

    private FileObject Seed(OwnerType owner, FolderType folder, Guid? ownerId, string? resourceType, string? role)
    {
        var key = ObjectKey.Create($"tenants/{Tenant:N}/{owner}/files/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                Tenant,
                owner,
                ownerId,
                folder,
                2025,
                key,
                "doc.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(1)
            )
            .Value;
        if (resourceType is not null)
            file.DeclareSource(
                FileSourceDeclaration.Create("tasks", resourceType, Guid.NewGuid(), role, null).Value,
                DateTime.UtcNow
            );
        file.MarkPendingScan(DateTime.UtcNow);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        _files.Seed(file);
        _library.Add(
            Tenant,
            new LibraryRow(
                file.Id,
                file.OriginalName,
                "application/pdf",
                file.SizeBytes,
                file.CreatedAtUtc,
                file.CreatedBy,
                file.TaxYear,
                file.OwnerType,
                file.OwnerId,
                file.FolderType,
                file.FolderId,
                file.SourceResourceType,
                file.SourceResourceId,
                file.SourceRole,
                file.RelatedCustomerId
            )
        );
        return file;
    }

    private Task<Result<DownloadUrlResponse>> Issue(FileObject file)
    {
        var bus = new FakeMessageBus { OnInvokeWithResult = DelegateToExistingHandler };
        var access = LibraryAccessPolicyTests.Access(
            readable: [LibrarySource.Documents, LibrarySource.Tasks],
            explorerRestrict: true,
            assigned: [Guid.NewGuid()]
        );
        return IssueLibraryDownloadUrlHandler.Handle(
            new IssueLibraryDownloadUrlQuery(
                Tenant,
                Guid.NewGuid(),
                access,
                new StorageActorScope(false, null),
                file.Id,
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            _library,
            _owners,
            bus,
            CancellationToken.None
        );
    }

    private async Task<object?> DelegateToExistingHandler(object message)
    {
        var query = (IssueDownloadUrlQuery)message;
        _delegated.Add(query);
        return await IssueDownloadUrlHandler.Handle(
            query,
            _files,
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            Options.Create(new CloudStorageOptions()),
            new FakeStoragePlatformSettingsProvider(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );
    }
}
