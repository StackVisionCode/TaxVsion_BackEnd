using System.Reflection;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Files.LegalHold;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Application.Legal;
using TaxVision.CloudStorage.Application.Moderation;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Legal;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;
using TaxVision.CloudStorage.Infrastructure.Storage;
using TaxVision.CloudStorage.Tests.Domain;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// BB-16 en CloudStorage. Estos handlers entran por <c>InvokeAsync</c>: lo publicado antes de un guardado que falla
/// sale igual en el reintento en línea. El guardado falla siempre y no puede haber salido nada.
/// </summary>
public sealed class CloudStorageSaveBeforePublishTests
{
    private const string Pdf = "application/pdf";
    private static readonly StorageActorScope Staff = new(false, null);
    private static readonly RequestAuditContext Audit = new("127.0.0.1", "tests", "corr-bb16");

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly FakeFileObjectRepository _files = new();
    private readonly FakeFolderRepository _folders = new();
    private readonly FakeStorageLimitRepository _limits = new();
    private readonly FakeShareLinkRepository _shares = new();
    private readonly FakeDmcaNoticeRepository _notices = new();
    private readonly FakeObjectStorage _storage = new();
    private readonly FakeUnitOfWork _unitOfWork = new() { FailSaveWhen = () => true };
    private readonly FakeMessageBus _bus = new();

    public CloudStorageSaveBeforePublishTests() =>
        _limits.Seed(TenantStorageLimit.Create(_tenant, "starter", 100_000_000, 100_000_000));

    private object[] Services() =>
        [
            _files,
            _folders,
            _limits,
            _shares,
            _notices,
            _storage,
            new FakeMultipartUploadStorage(),
            new FakeStorageAuditRepository(),
            new FakeShareLinkPasswordHasher(),
            new DefaultObjectKeyBuilder(),
            new SystemFolderProvisioner(_folders),
            Options.Create(new CloudStorageOptions()),
            new FakeStoragePlatformSettingsProvider(),
            new FakeUploadPolicyResolver(),
            new FakeSystemClock(DateTime.UtcNow),
            _unitOfWork,
            _bus,
        ];

    // Invoca el Handle del mensaje resolviendo sus parámetros por tipo.
    private async Task HandleAsync(Type handler, object message)
    {
        var method = handler
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate =>
                candidate.Name == "Handle" && candidate.GetParameters()[0].ParameterType == message.GetType()
            );
        var services = Services();
        var arguments = method
            .GetParameters()
            .Select(parameter =>
                parameter.Position == 0 ? message
                : parameter.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                : services.FirstOrDefault(parameter.ParameterType.IsInstanceOfType)
                    ?? throw new InvalidOperationException($"Sin doble para {parameter.ParameterType.Name}.")
            )
            .ToArray();

        await (Task)method.Invoke(null, arguments)!;
    }

    private async Task AssertNothingLeavesWhenTheSaveFailsAsync(Type handler, object message)
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(handler, message));

        Assert.Equal("Simulated save failure.", failure.Message);
        Assert.Empty(_bus.Published);
    }

    private FileObject Uploaded()
    {
        var key = ObjectKey.Create($"tenants/{_tenant:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                _tenant,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "return.pdf",
                Pdf,
                10,
                _actor,
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        _files.Seed(file);
        return file;
    }

    private FileObject Available()
    {
        var file = Uploaded();
        Assert.True(file.MarkPendingScan(DateTime.UtcNow).IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        Assert.True(
            file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, Pdf, DateTime.UtcNow).IsSuccess
        );
        _files.Seed(file);
        return file;
    }

    private (ShareLink Link, string Token) Link(
        FileObject file,
        ShareVisibility visibility = ShareVisibility.Public,
        string? passwordHash = null,
        DateTime? expiresAtUtc = null
    )
    {
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                _tenant,
                file.Id,
                ShareResourceType.File,
                visibility,
                SharePermission.Download,
                passwordHash,
                expiresAtUtc,
                null,
                _actor,
                DateTime.UtcNow.AddDays(-2)
            )
            .Value;
        _shares.Seed(link);
        return (link, token);
    }

    private DmcaNotice Notice(FileObject file)
    {
        var notice = DmcaNotice
            .Register(
                Guid.NewGuid(),
                _tenant,
                file.Id,
                "Acme Rights Holder",
                ClaimantEmail.Create("legal@acme.example").Value,
                "desc",
                "desc",
                true,
                _actor,
                DateTime.UtcNow
            )
            .Value;
        _notices.Seed(notice);
        return notice;
    }

    [Fact]
    public Task InitiateUpload() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(InitiateUploadHandler),
            new InitiateUploadCommand(
                _tenant,
                _actor,
                Staff,
                new InitiateUploadRequest("a.pdf", Pdf, 10, OwnerType.Tenant, null, FolderType.Documents, 2025),
                Audit
            )
        );

    [Fact]
    public Task CompleteUpload()
    {
        var file = Uploaded();
        _storage.ObjectSizes[(new CloudStorageOptions().TempBucket, file.ObjectKey)] = file.SizeBytes;

        return AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(CompleteUploadHandler),
            new CompleteUploadCommand(_tenant, _actor, Staff, file.Id, Audit)
        );
    }

    [Fact]
    public Task DeleteFile() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(DeleteFileHandler),
            new DeleteFileCommand(_tenant, _actor, Staff, Available().Id, Audit)
        );

    [Fact]
    public Task CopyFile() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(CopyFileHandler),
            new CopyFileCommand(
                _tenant,
                _actor,
                Staff,
                Available().Id,
                new CopyFileRequest(OwnerType.Customer, Guid.NewGuid(), FolderType.Documents, 2026),
                Audit
            )
        );

    [Fact]
    public Task InitiateMultipartUpload() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(InitiateMultipartUploadHandler),
            new InitiateMultipartUploadCommand(
                _tenant,
                _actor,
                Staff,
                new InitiateMultipartUploadRequest(
                    "a.pdf",
                    Pdf,
                    12L * 1024 * 1024,
                    OwnerType.Tenant,
                    null,
                    FolderType.Documents,
                    2025
                ),
                Audit
            )
        );

    [Fact]
    public Task SetLegalHold() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(SetLegalHoldHandler),
            new SetLegalHoldCommand(_tenant, _actor, Available().Id, "litigation", Audit)
        );

    [Fact]
    public Task LiftLegalHold()
    {
        var file = Available();
        Assert.True(file.PlaceLegalHold().IsSuccess);

        return AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(LiftLegalHoldHandler),
            new LiftLegalHoldCommand(_tenant, _actor, file.Id, "resolved", Audit)
        );
    }

    [Fact]
    public Task IssueDownloadUrl() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(IssueDownloadUrlHandler),
            new IssueDownloadUrlQuery(_tenant, _actor, Staff, Available().Id, Audit)
        );

    [Fact]
    public Task PrepareZipDownload() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(PrepareZipDownloadHandler),
            new PrepareZipDownloadQuery(_tenant, _actor, Staff, [Available().Id, Available().Id], [], Audit)
        );

    [Fact]
    public Task RestoreFile()
    {
        var file = Available();
        Assert.True(file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);

        return AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(RestoreFileHandler),
            new RestoreFileCommand(_tenant, _actor, file.Id, Audit)
        );
    }

    private Folder Folder()
    {
        var folder = TaxVision
            .CloudStorage.Domain.Folders.Folder.Create(
                Guid.NewGuid(),
                _tenant,
                OwnerType.Tenant,
                null,
                null,
                FolderName.Create("Docs").Value,
                null,
                _actor,
                DateTime.UtcNow
            )
            .Value;
        _folders.Seed(folder);
        return folder;
    }

    [Fact]
    public Task RestoreFolder()
    {
        var folder = Folder();
        var file = Available();
        Assert.True(file.MoveToFolder(folder.Id, DateTime.UtcNow).IsSuccess);
        Assert.True(file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30), folder.Id).IsSuccess);
        Assert.True(folder.SoftDelete(folder.Id, DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);

        return AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(RestoreFolderHandler),
            new RestoreFolderCommand(_tenant, _actor, folder.Id, Audit)
        );
    }

    [Fact]
    public Task DeleteFolder()
    {
        var folder = Folder();
        var file = Available();
        Assert.True(file.MoveToFolder(folder.Id, DateTime.UtcNow).IsSuccess);

        return AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(DeleteFolderHandler),
            new DeleteFolderCommand(_tenant, _actor, Staff, folder.Id, Audit)
        );
    }

    [Fact]
    public Task RegisterDmcaTakedown() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(RegisterDmcaTakedownHandler),
            new RegisterDmcaTakedownCommand(
                _tenant,
                _actor,
                Available().Id,
                "Acme Rights Holder",
                "legal@acme.example",
                "A photograph",
                "The same photograph",
                true,
                Audit
            )
        );

    [Fact]
    public Task SubmitDmcaCounterNotice()
    {
        var file = Available();
        Assert.True(file.BlockForTakedown(DateTime.UtcNow).IsSuccess);

        return AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(SubmitDmcaCounterNoticeHandler),
            new SubmitDmcaCounterNoticeCommand(_tenant, _actor, Notice(file).Id, "Mine.", Staff, Audit)
        );
    }

    [Fact]
    public Task ReinstateDmcaFile()
    {
        var file = Available();
        Assert.True(file.BlockForTakedown(DateTime.UtcNow).IsSuccess);
        var notice = Notice(file);
        Assert.True(notice.SubmitCounterNotice("Mine.", _actor, DateTime.UtcNow).IsSuccess);

        return AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ReinstateDmcaFileHandler),
            new ReinstateDmcaFileCommand(_tenant, _actor, notice.Id, "Resolved", Audit)
        );
    }

    private FileObject PendingReview()
    {
        var file = FileObjectReviewTransitionTests.PendingReview(_tenant);
        _files.Seed(file);
        return file;
    }

    [Fact]
    public Task ApproveContentReview() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ApproveContentReviewHandler),
            new ApproveContentReviewCommand(_tenant, PendingReview().Id, _actor, Audit)
        );

    [Fact]
    public Task RejectContentReview() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(RejectContentReviewHandler),
            new RejectContentReviewCommand(_tenant, PendingReview().Id, _actor, "Violation", Audit)
        );

    [Fact]
    public Task CreateShareLink() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(CreateShareLinkHandler),
            new CreateShareLinkCommand(
                _tenant,
                _actor,
                Staff,
                false,
                Available().Id,
                ShareVisibility.TenantOnly,
                SharePermission.Download,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            )
        );

    [Fact]
    public Task RevokeShareLink() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(RevokeShareLinkHandler),
            new RevokeShareLinkCommand(_tenant, _actor, Link(Available()).Link.Id, Audit)
        );

    [Fact]
    public Task ChangeSharePermission() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ChangeSharePermissionHandler),
            new ChangeSharePermissionCommand(
                _tenant,
                _actor,
                true,
                Link(Available(), ShareVisibility.TenantOnly).Link.Id,
                SharePermission.View,
                Audit
            )
        );

    private ResolvePublicShareQuery PublicQuery(string token, string? password = null) =>
        new(token, password, null, null, "127.0.0.1", "tests");

    [Fact]
    public Task ResolvePublicShare_Accessed() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ResolvePublicShareHandler),
            PublicQuery(Link(Available()).Token)
        );

    [Fact]
    public Task ResolvePublicShare_WrongPassword() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ResolvePublicShareHandler),
            PublicQuery(Link(Available(), passwordHash: "hashed:right").Token, "wrong")
        );

    [Fact]
    public Task ResolvePublicShare_ResourceNotAvailable()
    {
        var file = Available();
        var token = Link(file).Token;
        Assert.True(file.BlockForTakedown(DateTime.UtcNow).IsSuccess);

        return AssertNothingLeavesWhenTheSaveFailsAsync(typeof(ResolvePublicShareHandler), PublicQuery(token));
    }

    [Fact]
    public Task ResolvePublicShare_ExpiredLink() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ResolvePublicShareHandler),
            PublicQuery(Link(Available(), expiresAtUtc: DateTime.UtcNow.AddDays(-1)).Token)
        );

    [Fact]
    public Task ResolvePublicShare_VisibilityNotPublic() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ResolvePublicShareHandler),
            PublicQuery(Link(Available(), ShareVisibility.TenantOnly).Token)
        );

    private ResolvePrivateShareQuery PrivateQuery(string token, Guid? tenantId = null) =>
        new(token, tenantId ?? _tenant, _actor, Staff, null, "127.0.0.1", "tests");

    [Fact]
    public Task ResolvePrivateShare_Accessed() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ResolvePrivateShareHandler),
            PrivateQuery(Link(Available(), ShareVisibility.TenantOnly).Token)
        );

    [Fact]
    public Task ResolvePrivateShare_ExpiredLink() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ResolvePrivateShareHandler),
            PrivateQuery(Link(Available(), ShareVisibility.TenantOnly, expiresAtUtc: DateTime.UtcNow.AddDays(-1)).Token)
        );

    [Fact]
    public Task ResolvePrivateShare_TenantMismatch() =>
        AssertNothingLeavesWhenTheSaveFailsAsync(
            typeof(ResolvePrivateShareHandler),
            PrivateQuery(Link(Available(), ShareVisibility.TenantOnly).Token, Guid.NewGuid())
        );

    [Fact]
    public Task ResolvePrivateShare_ResourceNotAvailable()
    {
        var file = Available();
        var token = Link(file, ShareVisibility.TenantOnly).Token;
        Assert.True(file.BlockForTakedown(DateTime.UtcNow).IsSuccess);

        return AssertNothingLeavesWhenTheSaveFailsAsync(typeof(ResolvePrivateShareHandler), PrivateQuery(token));
    }
}
