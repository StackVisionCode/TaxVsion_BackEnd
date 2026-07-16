using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Fase C3 — Share links: creacion/revocacion/expiracion (autenticado) y
/// resolucion de token (publico/privado). Incluye los tests obligatorios de la
/// guia: link privado de otro tenant -> Denied; publico expirado/revocado/agotado
/// -> Denied.
/// </summary>
public sealed class ShareLinkHandlerTests
{
    private static readonly StorageActorScope TenantScope = new(false, null);
    private static readonly RequestAuditContext Audit = new(null, null, "corr-1");

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
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        return file;
    }

    private static IOptions<CloudStorageOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new CloudStorageOptions());

    // ---------- CreateShareLinkHandler ----------

    [Fact]
    public async Task Create_succeeds_and_returns_the_plain_token_once()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var shares = new FakeShareLinkRepository();
        var limits = new FakeStorageLimitRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                false,
                file.Id,
                ShareVisibility.TenantOnly,
                SharePermission.Download,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            ),
            files,
            shares,
            limits,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.PlainToken);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Create_rejects_Public_visibility_when_the_tenant_has_not_enabled_it()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(tenantId, "starter", 1000, 1000)); // AllowPublicShareLinks=false por defecto

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                false,
                file.Id,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            ),
            files,
            new FakeShareLinkRepository(),
            limits,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.PublicSharingDisabled, result.Error);
    }

    [Fact]
    public async Task Create_allows_Public_visibility_once_the_tenant_has_enabled_it()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limit = TenantStorageLimit.Create(tenantId, "starter", 1000, 1000);
        limit.EnablePublicSharing();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                false,
                file.Id,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            ),
            files,
            new FakeShareLinkRepository(),
            limits,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Create_rejects_Upload_permission_without_the_manage_claim()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                false, // ActorHasManagePermission
                file.Id,
                ShareVisibility.TenantOnly,
                SharePermission.Upload,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            ),
            files,
            new FakeShareLinkRepository(),
            new FakeStorageLimitRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.ElevatedPermissionRequiresManage, result.Error);
    }

    [Fact]
    public async Task Create_allows_Upload_permission_when_the_actor_has_the_manage_claim()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                true,
                file.Id,
                ShareVisibility.TenantOnly,
                SharePermission.Upload,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            ),
            files,
            new FakeShareLinkRepository(),
            new FakeStorageLimitRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Create_rejects_Upload_permission_on_a_Public_link_even_with_the_manage_claim()
    {
        // Fase C4 (completitud) — §20.4 del plan: "en un PublicLink, nunca Upload/Edit/ShareAgain".
        // Antes de este fix, tener cloudstorage.share.manage bastaba para combinar
        // Visibility.Public con Permission.Upload/EditMetadata.
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                true, // ActorHasManagePermission — no alcanza, Public nunca admite Upload/EditMetadata
                file.Id,
                ShareVisibility.Public,
                SharePermission.Upload,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            ),
            files,
            new FakeShareLinkRepository(),
            new FakeStorageLimitRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.ElevatedPermissionNotAllowedOnPublicLink, result.Error);
    }

    [Fact]
    public async Task Create_rejects_SpecificUsers_visibility_without_any_recipient()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                false,
                file.Id,
                ShareVisibility.SpecificUsers,
                SharePermission.View,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            ),
            files,
            new FakeShareLinkRepository(),
            new FakeStorageLimitRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.RecipientsRequired, result.Error);
    }

    [Fact]
    public async Task Create_of_a_file_belonging_to_another_tenant_is_not_found()
    {
        var file = AvailableFile(Guid.NewGuid());
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TenantScope,
                false,
                file.Id,
                ShareVisibility.TenantOnly,
                SharePermission.View,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit
            ),
            files,
            new FakeShareLinkRepository(),
            new FakeStorageLimitRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    // ---------- RevokeShareLinkHandler / UpdateShareExpirationHandler ----------

    private static ShareLink SeededLink(
        Guid tenantId,
        ShareVisibility visibility = ShareVisibility.Public,
        int? maxAccessCount = null,
        string? passwordHash = null,
        DateTime? expiresAtUtc = null
    ) =>
        ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                Guid.NewGuid(),
                ShareResourceType.File,
                visibility,
                SharePermission.View,
                passwordHash,
                expiresAtUtc,
                maxAccessCount,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value.Link;

    [Fact]
    public async Task Revoke_succeeds_for_an_active_link()
    {
        var tenantId = Guid.NewGuid();
        var link = SeededLink(tenantId);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var unitOfWork = new FakeUnitOfWork();

        var result = await RevokeShareLinkHandler.Handle(
            new RevokeShareLinkCommand(tenantId, Guid.NewGuid(), link.Id, Audit),
            shares,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Revoke_of_a_link_belonging_to_another_tenant_is_not_found()
    {
        var link = SeededLink(Guid.NewGuid());
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);

        var result = await RevokeShareLinkHandler.Handle(
            new RevokeShareLinkCommand(Guid.NewGuid(), Guid.NewGuid(), link.Id, Audit),
            shares,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateExpiration_rejects_a_date_in_the_past()
    {
        var tenantId = Guid.NewGuid();
        var link = SeededLink(tenantId);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);

        var result = await UpdateShareExpirationHandler.Handle(
            new UpdateShareExpirationCommand(tenantId, link.Id, DateTime.UtcNow.AddDays(-1)),
            shares,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.ExpirationInPast, result.Error);
    }

    // ---------- ChangeSharePermissionHandler (Fase C4 completitud) ----------

    [Fact]
    public async Task ChangePermission_updates_the_permission_audits_and_publishes_an_event()
    {
        var tenantId = Guid.NewGuid();
        var link = SeededLink(tenantId, ShareVisibility.TenantOnly);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var audit = new FakeStorageAuditRepository();
        var bus = new FakeMessageBus();

        var result = await ChangeSharePermissionHandler.Handle(
            new ChangeSharePermissionCommand(tenantId, Guid.NewGuid(), true, link.Id, SharePermission.Download, Audit),
            shares,
            new FakeStorageLimitRepository(),
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            bus,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SharePermission.Download, link.Permission);
        Assert.Single(audit.Logs, log => log.Action == "share.permission-changed");
        var evt = Assert.Single(bus.Published.OfType<ShareLinkPermissionChangedIntegrationEvent>());
        Assert.Equal("View", evt.OldPermission);
        Assert.Equal("Download", evt.NewPermission);
    }

    [Fact]
    public async Task ChangePermission_to_the_same_value_is_a_no_op_and_publishes_nothing()
    {
        var tenantId = Guid.NewGuid();
        var link = SeededLink(tenantId, ShareVisibility.TenantOnly); // Permission=View por defecto en SeededLink
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var audit = new FakeStorageAuditRepository();
        var bus = new FakeMessageBus();

        var result = await ChangeSharePermissionHandler.Handle(
            new ChangeSharePermissionCommand(tenantId, Guid.NewGuid(), true, link.Id, SharePermission.View, Audit),
            shares,
            new FakeStorageLimitRepository(),
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            bus,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(audit.Logs);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task ChangePermission_rejects_Upload_on_a_Public_link_even_with_the_manage_claim()
    {
        var tenantId = Guid.NewGuid();
        var link = SeededLink(tenantId, ShareVisibility.Public);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);

        var result = await ChangeSharePermissionHandler.Handle(
            new ChangeSharePermissionCommand(tenantId, Guid.NewGuid(), true, link.Id, SharePermission.Upload, Audit),
            shares,
            new FakeStorageLimitRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.ElevatedPermissionNotAllowedOnPublicLink, result.Error);
        Assert.Equal(SharePermission.View, link.Permission); // sin cambios
    }

    // ---------- ResolvePublicShareHandler ----------

    private static async Task<ShareAccessResult> ResolvePublic(
        string token,
        FakeShareLinkRepository shares,
        FakeFileObjectRepository files,
        string? password = null,
        string? email = null,
        Guid? fileId = null,
        FakeFolderRepository? folders = null
    ) =>
        await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, password, email, fileId, "127.0.0.1", "test-agent"),
            shares,
            files,
            folders ?? new FakeFolderRepository(),
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    [Fact]
    public async Task ResolvePublic_redirects_for_an_active_Public_link()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var link = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link.Link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await ResolvePublic(link.PlainToken, shares, files);

        Assert.Equal(ShareAccessOutcome.Redirect, result.Outcome);
        Assert.Equal(1, link.Link.AccessCount);
    }

    [Theory]
    [InlineData(SharePermission.View, "inline")]
    [InlineData(SharePermission.Preview, "inline")]
    [InlineData(SharePermission.Download, "attachment")]
    public async Task ResolvePublic_sets_ContentDisposition_from_the_link_permission(
        SharePermission permission,
        string expectedKind
    )
    {
        // Fase C4 (completitud) — antes View/Preview/Download producian exactamente
        // la misma presigned URL; ahora View/Preview fuerzan "inline" (se renderiza
        // en el browser) y Download fuerza "attachment" (descarga forzada).
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var link = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.Public,
                permission,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link.Link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var storage = new FakeObjectStorage();

        var result = await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(link.PlainToken, null, null, null, null, null),
            shares,
            files,
            new FakeFolderRepository(),
            storage,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Redirect, result.Outcome);
        var presigned = Assert.Single(storage.PresignedWithDisposition);
        Assert.StartsWith(expectedKind, presigned.ContentDisposition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvePublic_is_denied_for_an_unknown_token()
    {
        var result = await ResolvePublic(
            "does-not-exist",
            new FakeShareLinkRepository(),
            new FakeFileObjectRepository()
        );

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_is_denied_once_the_link_is_revoked()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        link.Revoke(DateTime.UtcNow);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await ResolvePublic(token, shares, files);

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_is_denied_once_expired()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var now = DateTime.UtcNow;
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                now.AddMinutes(1),
                null,
                Guid.NewGuid(),
                now
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var bus = new FakeMessageBus();
        var result = await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, null, null, null, null, null),
            shares,
            files,
            new FakeFolderRepository(),
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now.AddMinutes(5)), // ya vencido
            bus,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
        Assert.Single(bus.Published.OfType<ShareLinkExpiredIntegrationEvent>());
        Assert.Single(bus.Published.OfType<ShareLinkAccessDeniedIntegrationEvent>());
    }

    [Fact]
    public async Task ResolvePublic_is_denied_once_exhausted()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                1,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        link.RegisterAccess(DateTime.UtcNow); // consume el unico acceso permitido
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await ResolvePublic(token, shares, files);

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_requires_and_verifies_the_password_when_the_link_has_one()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var hasher = new FakeShareLinkPasswordHasher();
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.Public,
                SharePermission.View,
                hasher.Hash("secret"),
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var withoutPassword = await ResolvePublic(token, shares, files);
        var withWrongPassword = await ResolvePublic(token, shares, files, password: "wrong");
        var withRightPassword = await ResolvePublic(token, shares, files, password: "secret");

        Assert.Equal(ShareAccessOutcome.PasswordRequired, withoutPassword.Outcome);
        Assert.Equal(ShareAccessOutcome.Denied, withWrongPassword.Outcome);
        Assert.Equal(ShareAccessOutcome.Redirect, withRightPassword.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_serves_ExternalRecipients_only_when_the_email_matches()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.ExternalRecipients,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        link.AddExternalRecipient("client@example.com");
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var withoutEmail = await ResolvePublic(token, shares, files);
        var withWrongEmail = await ResolvePublic(token, shares, files, email: "someone-else@example.com");
        var withMatchingEmail = await ResolvePublic(token, shares, files, email: "client@example.com");

        Assert.Equal(ShareAccessOutcome.Denied, withoutEmail.Outcome);
        Assert.Equal(ShareAccessOutcome.Denied, withWrongEmail.Outcome);
        Assert.Equal(ShareAccessOutcome.Redirect, withMatchingEmail.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_never_serves_a_TenantOnly_link()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.TenantOnly,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await ResolvePublic(token, shares, files);

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
    }

    // ---------- ResolvePrivateShareHandler ----------

    private static async Task<ShareAccessResult> ResolvePrivate(
        string token,
        Guid jwtTenantId,
        Guid jwtUserId,
        StorageActorScope scope,
        FakeShareLinkRepository shares,
        FakeFileObjectRepository files,
        Guid? fileId = null,
        FakeFolderRepository? folders = null
    ) =>
        await ResolvePrivateShareHandler.Handle(
            new ResolvePrivateShareQuery(token, jwtTenantId, jwtUserId, scope, fileId, "127.0.0.1", "test-agent"),
            shares,
            files,
            folders ?? new FakeFolderRepository(),
            new FakeObjectStorage(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    [Fact]
    public async Task ResolvePrivate_is_denied_when_the_JWT_tenant_does_not_match_the_link_tenant()
    {
        // Test obligatorio: el token es valido, pero un actor de OTRO tenant lo usa -> Denied, fail-closed.
        var linkTenantId = Guid.NewGuid();
        var file = AvailableFile(linkTenantId);
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                linkTenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.TenantOnly,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await ResolvePrivate(token, Guid.NewGuid(), Guid.NewGuid(), TenantScope, shares, files);

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task ResolvePrivate_allows_TenantOnly_for_any_authenticated_actor_of_the_same_tenant()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.TenantOnly,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await ResolvePrivate(token, tenantId, Guid.NewGuid(), TenantScope, shares, files);

        Assert.Equal(ShareAccessOutcome.Redirect, result.Outcome);
    }

    [Fact]
    public async Task ResolvePrivate_never_serves_a_Public_link()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await ResolvePrivate(token, tenantId, Guid.NewGuid(), TenantScope, shares, files);

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task ResolvePrivate_SpecificUsers_only_authorizes_the_listed_recipient()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var authorizedUserId = Guid.NewGuid();
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.SpecificUsers,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        link.AddUserRecipient(authorizedUserId);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var authorized = await ResolvePrivate(token, tenantId, authorizedUserId, TenantScope, shares, files);
        var notAuthorized = await ResolvePrivate(token, tenantId, Guid.NewGuid(), TenantScope, shares, files);

        Assert.Equal(ShareAccessOutcome.Redirect, authorized.Outcome);
        Assert.Equal(ShareAccessOutcome.Denied, notAuthorized.Outcome);
    }

    [Fact]
    public async Task ResolvePrivate_TenantCustomers_restricted_only_authorizes_the_listed_customer()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var authorizedCustomerId = Guid.NewGuid();
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.TenantCustomers,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        link.AddCustomerRecipient(authorizedCustomerId);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var authorizedScope = new StorageActorScope(true, authorizedCustomerId);
        var otherCustomerScope = new StorageActorScope(true, Guid.NewGuid());

        var authorized = await ResolvePrivate(token, tenantId, Guid.NewGuid(), authorizedScope, shares, files);
        var notAuthorized = await ResolvePrivate(token, tenantId, Guid.NewGuid(), otherCustomerScope, shares, files);
        var notACustomer = await ResolvePrivate(token, tenantId, Guid.NewGuid(), TenantScope, shares, files);

        Assert.Equal(ShareAccessOutcome.Redirect, authorized.Outcome);
        Assert.Equal(ShareAccessOutcome.Denied, notAuthorized.Outcome);
        Assert.Equal(ShareAccessOutcome.Denied, notACustomer.Outcome);
    }

    [Fact]
    public async Task ResolvePrivate_TenantCustomers_open_to_all_when_it_has_no_recipients()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                ShareResourceType.File,
                ShareVisibility.TenantCustomers,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var anyCustomerScope = new StorageActorScope(true, Guid.NewGuid());
        var result = await ResolvePrivate(token, tenantId, Guid.NewGuid(), anyCustomerScope, shares, files);

        Assert.Equal(ShareAccessOutcome.Redirect, result.Outcome);
    }

    // ---------- ListShareLinksForFileHandler / ListSharedWithMeHandler ----------

    [Fact]
    public async Task ListForFile_is_not_found_for_a_file_owned_by_another_tenant()
    {
        var file = AvailableFile(Guid.NewGuid());
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await ListShareLinksForFileHandler.Handle(
            new ListShareLinksForFileQuery(Guid.NewGuid(), TenantScope, file.Id),
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task SharedWithMe_only_returns_links_accessible_to_the_calling_actor()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantOnly = SeededLink(tenantId, ShareVisibility.TenantOnly);
        var specificToMe = SeededLink(tenantId, ShareVisibility.SpecificUsers);
        specificToMe.AddUserRecipient(userId);
        var specificToSomeoneElse = SeededLink(tenantId, ShareVisibility.SpecificUsers);
        specificToSomeoneElse.AddUserRecipient(Guid.NewGuid());
        var shares = new FakeShareLinkRepository();
        shares.Seed(tenantOnly);
        shares.Seed(specificToMe);
        shares.Seed(specificToSomeoneElse);

        var result = await ListSharedWithMeHandler.Handle(
            new ListSharedWithMeQuery(tenantId, userId, TenantScope, 0, 50),
            shares,
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Id == tenantOnly.Id);
        Assert.Contains(result, r => r.Id == specificToMe.Id);
    }
}
