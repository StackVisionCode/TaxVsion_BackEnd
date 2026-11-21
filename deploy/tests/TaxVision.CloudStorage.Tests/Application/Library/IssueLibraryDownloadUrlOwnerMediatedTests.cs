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

/// <summary>
/// La biblioteca entrega un archivo mediado por su dueño (dueño Communication) solo tras su confirmación, con la
/// compuerta encendida. Se prueba con la definición de chat, que todavía no se ofrece; el acceso se arma a mano.
/// </summary>
public sealed class IssueLibraryDownloadUrlOwnerMediatedTests
{
    private readonly FakeFileObjectRepository _files = new();
    private readonly FakeLibraryReadRepository _library = new();
    private readonly FakeFileOwnerVisibilityClient _owners = new();
    private readonly List<IssueDownloadUrlQuery> _delegated = [];

    [Fact]
    public async Task A_confirmed_chat_attachment_gets_a_url_with_the_gate_on()
    {
        var file = SeedChatAttachment();
        _owners.Confirm(Visible(file.Id));

        var result = await Issue(file);

        Assert.True(result.IsSuccess);
        var delegated = Assert.Single(_delegated);
        Assert.False(delegated.Scope.HidesOwnerMediated);
        Assert.Equal(FileVisibilityPurposes.Download, Assert.Single(_owners.Calls).Purpose);
        Assert.Equal(FileOwnerService.Communication, _owners.Calls[0].Owner);
    }

    [Fact]
    public async Task An_unconfirmed_chat_attachment_is_not_found()
    {
        var file = SeedChatAttachment();

        var result = await Issue(file);

        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Empty(_delegated);
    }

    [Fact]
    public async Task A_confirmed_file_without_download_is_not_found()
    {
        var file = SeedChatAttachment();
        _owners.Confirm(Visible(file.Id) with { CanDownload = false });

        var result = await Issue(file);

        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Empty(_delegated);
    }

    [Fact]
    public async Task Outside_the_library_the_same_file_is_still_not_found()
    {
        var file = SeedChatAttachment();

        var result = await DelegateAsync(
            new IssueDownloadUrlQuery(file.TenantId, Guid.NewGuid(), Employee(), file.Id, Audit())
        );

        Assert.Equal(FileErrors.NotFound, ((Result<DownloadUrlResponse>)result!).Error);
    }

    [Fact]
    public async Task A_note_attachment_confirmed_by_notes_gets_a_url_with_the_gate_on()
    {
        var file = FileReadGateNoteTests.NoteFile(OwnerType.Customer);
        Seed(file);
        _owners.Confirm(new VisibleFile(file.Id, FileVisibilityResourceTypes.Note, Guid.NewGuid(), "Note", null));

        var result = await Issue(file, [LibrarySource.Notes]);

        Assert.True(result.IsSuccess);
        Assert.False(Assert.Single(_delegated).Scope.HidesOwnerMediated);
        Assert.Equal(FileOwnerService.Notes, Assert.Single(_owners.Calls).Owner);
    }

    [Fact]
    public async Task A_sql_source_keeps_the_resolved_scope()
    {
        var file = OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.Documents, available: true);
        Seed(file);

        var result = await Issue(file, [LibrarySource.Documents]);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(_delegated).Scope.HidesOwnerMediated);
    }

    private FileObject SeedChatAttachment()
    {
        var file = OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Other, available: true);
        file.DeclareSource(
            FileSourceDeclaration.Create("communication", "conversation", Guid.NewGuid(), "attachment", null).Value,
            DateTime.UtcNow
        );
        Seed(file);
        return file;
    }

    private void Seed(FileObject file)
    {
        _files.Seed(file);
        _library.Add(
            file.TenantId,
            new LibraryRow(
                file.Id,
                file.OriginalName,
                file.DeclaredContentType,
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
    }

    private static VisibleFile Visible(Guid fileId) =>
        new(fileId, FileVisibilityResourceTypes.Conversation, Guid.NewGuid(), "Chat", null);

    private Task<Result<DownloadUrlResponse>> Issue(FileObject file, LibrarySource[]? readable = null) =>
        IssueLibraryDownloadUrlHandler.Handle(
            new IssueLibraryDownloadUrlQuery(
                file.TenantId,
                Guid.NewGuid(),
                LibraryAccessPolicyTests.Access(readable: readable ?? [LibrarySource.Chat], viewAll: true),
                Employee(),
                file.Id,
                Audit()
            ),
            _library,
            _owners,
            new FakeMessageBus { OnInvokeWithResult = DelegateAsync },
            CancellationToken.None
        );

    private async Task<object?> DelegateAsync(object message)
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

    // Empleado con la compuerta de archivos mediados encendida, como la deja el resolver de alcance.
    private static StorageActorScope Employee() => new(false, null) { HidesOwnerMediated = true };

    private static RequestAuditContext Audit() => new(null, null, Guid.NewGuid().ToString());
}
