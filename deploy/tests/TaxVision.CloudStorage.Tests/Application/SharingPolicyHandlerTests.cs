using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Platform;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Las reglas de compartir de la oficina se aplican al crear y al extender enlaces externos, y el bloqueo
/// por contraseñas fallidas corta los enlaces públicos de archivo y de carpeta. Los ajustes de plataforma
/// se cambian con auditoría y refrescan la caché.
/// </summary>
public sealed class SharingPolicyHandlerTests
{
    private static readonly StorageActorScope TenantScope = new(false, null);
    private static readonly RequestAuditContext Audit = new(null, null, "corr-1");
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task A_link_without_date_or_download_cap_takes_the_office_defaults()
    {
        var tenantId = Guid.NewGuid();
        var limit = TenantStorageLimit.Create(tenantId, "pro", 1000, 1000);
        limit.SetSharingPolicy(true, false, 3, 30, 8, 10, 25);
        var shares = new FakeShareLinkRepository();

        var result = await Create(
            tenantId,
            limit,
            shares,
            ShareVisibility.ExternalLink,
            password: null,
            expiresAtUtc: null
        );

        Assert.True(result.IsSuccess);
        var link = await shares.GetAsync(tenantId, result.Value.Link.Id, default);
        Assert.Equal(25, link!.MaxAccessCount);
        Assert.Equal(Now.AddDays(3).Date, link.ExpiresAtUtc.Date);
    }

    [Fact]
    public async Task Internal_links_keep_no_download_cap_and_short_passwords_are_rejected_everywhere()
    {
        var tenantId = Guid.NewGuid();
        var limit = TenantStorageLimit.Create(tenantId, "pro", 1000, 1000);
        limit.SetSharingPolicy(true, false, 7, 30, 10, 10, 25);
        var shares = new FakeShareLinkRepository();

        var internalLink = await Create(tenantId, limit, shares, ShareVisibility.TenantOnly, null, null);
        var shortPassword = await Create(tenantId, limit, shares, ShareVisibility.TenantOnly, "abc12", null);

        Assert.Null((await shares.GetAsync(tenantId, internalLink.Value.Link.Id, default))!.MaxAccessCount);
        Assert.Equal("ShareLink.PasswordTooShort", shortPassword.Error.Code);
    }

    [Fact]
    public async Task External_recipients_are_bound_by_the_maximum_lifetime()
    {
        var tenantId = Guid.NewGuid();
        var limit = TenantStorageLimit.Create(tenantId, "pro", 1000, 1000);
        limit.SetSharingPolicy(true, false, 7, 14, 8, 10, null);

        var result = await Create(
            tenantId,
            limit,
            new FakeShareLinkRepository(),
            ShareVisibility.ExternalRecipients,
            null,
            Now.AddDays(20),
            ["client@example.com"]
        );

        Assert.Equal(ShareErrors.ShareLifetimeExceedsMax, result.Error);
    }

    [Theory]
    [InlineData(ShareVisibility.Public, true)]
    [InlineData(ShareVisibility.TenantOnly, false)]
    public async Task Extending_an_external_link_cannot_pass_the_office_maximum(
        ShareVisibility visibility,
        bool rejected
    )
    {
        var tenantId = Guid.NewGuid();
        var limit = TenantStorageLimit.Create(tenantId, "pro", 1000, 1000);
        limit.SetSharingPolicy(true, false, 7, 30, 8, 10, null);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);
        var link = Link(tenantId, Guid.NewGuid(), ShareResourceType.File, visibility, null);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);

        var result = await UpdateShareExpirationHandler.Handle(
            new UpdateShareExpirationCommand(tenantId, link.Id, Now.AddDays(90)),
            shares,
            limits,
            new FakeSystemClock(Now),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(rejected, result.IsFailure);
        if (rejected)
            Assert.Equal(ShareErrors.ShareLifetimeExceedsMax, result.Error);
    }

    [Fact]
    public async Task A_public_file_link_locks_after_the_wrong_attempts_and_refuses_even_the_right_password()
    {
        var tenantId = Guid.NewGuid();
        var limit = TenantStorageLimit.Create(tenantId, "pro", 1000, 1000);
        limit.SetSharingPolicy(true, false, 7, 30, 8, 3, null);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var (link, token) = LinkWithToken(tenantId, file.Id, ShareResourceType.File, "hashed:right-password");
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);

        var outcomes = new List<ShareAccessOutcome>();
        foreach (var password in new[] { "nope-1", "nope-2", "nope-3", "right-password" })
            outcomes.Add((await ResolveFile(token, password, shares, files, limits)).Outcome);

        Assert.Equal(
            [
                ShareAccessOutcome.Denied,
                ShareAccessOutcome.Denied,
                ShareAccessOutcome.PasswordLocked,
                ShareAccessOutcome.PasswordLocked,
            ],
            outcomes
        );
    }

    [Fact]
    public async Task A_public_folder_link_locks_the_same_way()
    {
        var tenantId = Guid.NewGuid();
        var limit = TenantStorageLimit.Create(tenantId, "pro", 1000, 1000);
        limit.SetSharingPolicy(true, false, 7, 30, 8, 3, null);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);
        var (link, token) = LinkWithToken(tenantId, Guid.NewGuid(), ShareResourceType.Folder, "hashed:right-password");
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);

        PublicFolderOutcome last = default;
        foreach (var password in new[] { "nope-1", "nope-2", "nope-3" })
            last = (
                await ResolvePublicFolderContentsHandler.Handle(
                    new ResolvePublicFolderContentsQuery(token, password, null, null),
                    shares,
                    new FakeFileObjectRepository(),
                    new FakeFolderRepository(),
                    new FakeShareLinkPasswordHasher(),
                    limits,
                    new FakeStoragePlatformSettingsProvider(),
                    new FakeSystemClock(Now),
                    new FakeUnitOfWork(),
                    CancellationToken.None
                )
            ).Outcome;

        Assert.Equal(PublicFolderOutcome.PasswordLocked, last);
    }

    [Fact]
    public async Task Platform_settings_changes_are_audited_and_refresh_the_cache()
    {
        var settings = StoragePlatformSettings.Defaults();
        var repository = new SingleSettingsRepository(settings);
        var provider = new FakeStoragePlatformSettingsProvider(settings);
        var audits = new AuditOnlyCatalog();

        var result = await StoragePlatformSettingsHandler.Handle(
            new UpdateStoragePlatformSettingsCommand(
                10,
                3,
                12,
                60,
                800,
                80,
                3L * 1024 * 1024 * 1024,
                30,
                Guid.NewGuid()
            ),
            repository,
            provider,
            audits,
            new FakeSystemClock(Now),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(12, settings.UploadReservationHours);
        Assert.Equal("platform_settings", Assert.Single(audits.Audits).Kind);
        Assert.Equal(1, provider.Invalidations);
    }

    [Fact]
    public async Task Without_the_settings_row_the_provider_serves_the_seeded_defaults()
    {
        var provider = new StoragePlatformSettingsProvider(
            new SingleSettingsRepository(null),
            new StoragePlatformSettingsCache(),
            new FakeSystemClock(Now)
        );

        var settings = await provider.GetAsync(default);

        Assert.Equal(StoragePlatformSettings.Defaults().RecycleBinRetentionDays, settings.RecycleBinRetentionDays);
        Assert.Equal(StoragePlatformSettings.Defaults().ShareUrlMinutes, settings.ShareUrlMinutes);
    }

    private static Task<ShareAccessResult> ResolveFile(
        string token,
        string password,
        FakeShareLinkRepository shares,
        FakeFileObjectRepository files,
        FakeStorageLimitRepository limits
    ) =>
        ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, password, null, null, null, null),
            shares,
            files,
            new FakeFolderRepository(),
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            limits,
            new FakeStoragePlatformSettingsProvider(),
            new FakeStorageAuditRepository(),
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(Now),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    private static async Task<Result<CreatedShareLinkResponse>> Create(
        Guid tenantId,
        TenantStorageLimit limit,
        FakeShareLinkRepository shares,
        ShareVisibility visibility,
        string? password,
        DateTime? expiresAtUtc,
        IReadOnlyList<string>? recipientEmails = null
    )
    {
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);
        return await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                false,
                file.Id,
                visibility,
                SharePermission.Download,
                password,
                expiresAtUtc,
                null,
                [],
                [],
                recipientEmails ?? [],
                Audit
            ),
            files,
            shares,
            limits,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(Now),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );
    }

    private static ShareLink Link(
        Guid tenantId,
        Guid resourceId,
        ShareResourceType type,
        ShareVisibility visibility,
        string? passwordHash
    ) =>
        ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                resourceId,
                type,
                visibility,
                SharePermission.Download,
                passwordHash,
                Now.AddDays(7),
                null,
                Guid.NewGuid(),
                Now
            )
            .Value.Link;

    private static (ShareLink Link, string Token) LinkWithToken(
        Guid tenantId,
        Guid resourceId,
        ShareResourceType type,
        string passwordHash
    ) =>
        ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                resourceId,
                type,
                ShareVisibility.Public,
                SharePermission.Download,
                passwordHash,
                Now.AddDays(7),
                null,
                Guid.NewGuid(),
                Now
            )
            .Value;

    private static FileObject AvailableFile(Guid tenantId)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "return.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                Now,
                Now.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", Now);
        return file;
    }

    private sealed class SingleSettingsRepository(StoragePlatformSettings? settings)
        : IStoragePlatformSettingsRepository
    {
        public Task<StoragePlatformSettings?> GetAsync(CancellationToken ct) => Task.FromResult(settings);

        public Task<StoragePlatformSettings?> GetForUpdateAsync(CancellationToken ct) => Task.FromResult(settings);
    }

    private sealed class AuditOnlyCatalog : TaxVision.CloudStorage.Application.Quotas.IFilePolicyCatalogRepository
    {
        public List<TaxVision.CloudStorage.Domain.FileTypes.FilePolicyAudit> Audits { get; } = [];

        public Task<IReadOnlyList<TaxVision.CloudStorage.Domain.FileTypes.FileTypeDefinition>> ListFileTypesAsync(
            CancellationToken ct
        ) => Task.FromResult(TestFilePolicyCatalog.FileTypes());

        public Task<TaxVision.CloudStorage.Domain.FileTypes.FileTypeDefinition?> GetFileTypeAsync(
            string extension,
            CancellationToken ct
        ) => Task.FromResult<TaxVision.CloudStorage.Domain.FileTypes.FileTypeDefinition?>(null);

        public void AddFileType(TaxVision.CloudStorage.Domain.FileTypes.FileTypeDefinition definition) { }

        public Task<IReadOnlyList<TaxVision.CloudStorage.Domain.FileTypes.FolderFilePolicy>> ListFolderPoliciesAsync(
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<TaxVision.CloudStorage.Domain.FileTypes.FolderFilePolicy>>([]);

        public Task<TaxVision.CloudStorage.Domain.FileTypes.FolderFilePolicy?> GetFolderPolicyAsync(
            FolderType folderType,
            CancellationToken ct
        ) => Task.FromResult<TaxVision.CloudStorage.Domain.FileTypes.FolderFilePolicy?>(null);

        public void AddAudit(TaxVision.CloudStorage.Domain.FileTypes.FilePolicyAudit audit) => Audits.Add(audit);
    }
}
