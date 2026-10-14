using System.Security.Claims;
using BuildingBlocks.Authorization;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Storage;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.CustomerAccess;

/// <summary>
/// Fase 4 — las reglas <see cref="CustomerDocumentAccessRule"/> precargadas en el scope actúan
/// como deny en lectura (Visible/Preview oculta; GetById devuelve NotFound) y mutación (Download/
/// Upload/Delete devuelven cloudstorage.customer_access.denied con reason).
/// </summary>
public sealed class PerItemAccessEnforcementTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private static FileObject CustomerFile(Guid? ownerId = null, Guid? folderId = null)
    {
        var key = ObjectKey.Create($"tenants/{Tenant:N}/Customer/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                Tenant,
                OwnerType.Customer,
                ownerId ?? Customer,
                FolderType.Documents,
                2025,
                key,
                "doc.pdf",
                "application/pdf",
                100,
                Actor,
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        if (folderId is { } fid)
            file.MoveToFolder(fid, DateTime.UtcNow);
        return file;
    }

    private static Folder CustomerFolder(string name = "Documents", string? category = null)
    {
        var folderName = FolderName.Create(name).Value;
        var cat = category is null ? null : FolderCategory.Create(category).Value;
        return Folder
            .Create(
                Guid.NewGuid(),
                Tenant,
                OwnerType.Customer,
                Customer,
                parentFolderId: null,
                folderName,
                parentRelativePath: null,
                Actor,
                DateTime.UtcNow,
                cat
            )
            .Value;
    }

    private static CustomerDocumentAccessRule Rule(
        AccessResourceType type,
        Guid resourceId,
        AccessCapability cap,
        string? reason = null
    ) =>
        CustomerDocumentAccessRule
            .Create(Guid.NewGuid(), Tenant, Customer, type, resourceId, cap, Actor, DateTime.UtcNow, reason)
            .Value;

    private static StorageActorScope PortalScopeWith(params CustomerDocumentAccessRule[] rules)
    {
        var grouped = rules
            .GroupBy(r => (r.ResourceType, r.ResourceId))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<CustomerAccessRuleView>)g.Select(CustomerAccessRuleMapper.Map).ToList()
            );
        return new StorageActorScope(IsCustomerPortal: true, CustomerId: Customer)
        {
            TenantId = Tenant,
            DenyRules = grouped,
        };
    }

    private static CustomerDocumentAccessRule TenantScopeRule(AccessCapability cap, string? reason = null) =>
        Rule(AccessResourceType.TenantScope, Tenant, cap, reason);

    // ------------------------- Scope resolver -------------------------

    [Fact]
    public async Task Resolver_portal_flag_on_loads_rules_grouped_by_resource()
    {
        var folder = CustomerFolder();
        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(AccessResourceType.Folder, folder.Id, AccessCapability.Upload, "no uploads"));
        rules.Add(Rule(AccessResourceType.Folder, folder.Id, AccessCapability.Download));
        var resolver = NewResolver(rules, perItemEnabled: true);

        var scope = await resolver.ResolveAsync(
            NewPortalPrincipal(),
            Tenant,
            Actor,
            new StorageActorScope(true, Customer),
            CancellationToken.None
        );

        Assert.Single(scope.DenyRules);
        var set = scope.DenyRules[(AccessResourceType.Folder, folder.Id)];
        Assert.Equal(2, set.Count);
        Assert.True(scope.HasAnyPortalDenyRules);
    }

    [Fact]
    public async Task Resolver_portal_flag_off_leaves_scope_empty()
    {
        var folder = CustomerFolder();
        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(AccessResourceType.Folder, folder.Id, AccessCapability.Upload));
        var resolver = NewResolver(rules, perItemEnabled: false);

        var scope = await resolver.ResolveAsync(
            NewPortalPrincipal(),
            Tenant,
            Actor,
            new StorageActorScope(true, Customer),
            CancellationToken.None
        );

        Assert.Empty(scope.DenyRules);
        Assert.False(scope.HasAnyPortalDenyRules);
    }

    [Fact]
    public async Task Resolver_staff_never_loads_rules_even_with_flag_on()
    {
        var folder = CustomerFolder();
        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(AccessResourceType.Folder, folder.Id, AccessCapability.Upload));
        var resolver = NewResolver(rules, perItemEnabled: true);

        var scope = await resolver.ResolveAsync(
            NewStaffPrincipal(),
            Tenant,
            Actor,
            new StorageActorScope(false, null),
            CancellationToken.None
        );

        Assert.Empty(scope.DenyRules);
    }

    // ------------------------- CanAccess -------------------------

    [Fact]
    public void CanAccess_portal_hides_file_with_visible_deny()
    {
        var file = CustomerFile();
        var scope = PortalScopeWith(Rule(AccessResourceType.File, file.Id, AccessCapability.Visible));

        Assert.False(scope.CanAccess(file));
    }

    [Fact]
    public void CanAccess_portal_hides_folder_with_preview_deny()
    {
        var folder = CustomerFolder();
        var scope = PortalScopeWith(Rule(AccessResourceType.Folder, folder.Id, AccessCapability.Preview));

        Assert.False(scope.CanAccess(folder));
    }

    [Fact]
    public void CanAccess_portal_with_download_deny_still_true()
    {
        // Download deny no oculta; el handler de download aplica el 403 explícito.
        var file = CustomerFile();
        var scope = PortalScopeWith(Rule(AccessResourceType.File, file.Id, AccessCapability.Download));

        Assert.True(scope.CanAccess(file));
    }

    [Fact]
    public void CanAccess_staff_ignores_rules()
    {
        var file = CustomerFile();
        var scope = new StorageActorScope(false, null); // staff: HasAnyPortalDenyRules queda false

        Assert.True(scope.CanAccess(file));
    }

    // ------------------------- Query filter -------------------------

    [Fact]
    public async Task ListFiles_portal_hides_denied_items()
    {
        var files = new FakeFileObjectRepository();
        var a = CustomerFile();
        var b = CustomerFile();
        var c = CustomerFile();
        files.Seed(a);
        files.Seed(b);
        files.Seed(c);
        var scope = PortalScopeWith(Rule(AccessResourceType.File, b.Id, AccessCapability.Visible));

        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(Tenant, scope, null, null, 0, 50),
            files,
            CancellationToken.None
        );

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, f => f.Id == b.Id);
    }

    [Fact]
    public async Task GetFile_portal_with_visible_deny_returns_not_found()
    {
        var files = new FakeFileObjectRepository();
        var a = CustomerFile();
        files.Seed(a);
        var scope = PortalScopeWith(Rule(AccessResourceType.File, a.Id, AccessCapability.Visible));

        var result = await GetFileHandler.Handle(new GetFileQuery(Tenant, scope, a.Id), files, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task FolderContents_portal_with_denies_recomputes_counts()
    {
        var folder = CustomerFolder();
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var files = new FakeFileObjectRepository();
        var visible = CustomerFile(folderId: folder.Id);
        var denied = CustomerFile(folderId: folder.Id);
        var alsoVisible = CustomerFile(folderId: folder.Id);
        files.Seed(visible);
        files.Seed(denied);
        files.Seed(alsoVisible);
        var scope = PortalScopeWith(Rule(AccessResourceType.File, denied.Id, AccessCapability.Visible));

        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(Tenant, scope, folder.Id),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.Equal(2, result.FileCount);
        Assert.Equal(2, result.Files.Count);
        Assert.DoesNotContain(result.Files, f => f.Id == denied.Id);
    }

    // ------------------------- Download handler -------------------------

    [Fact]
    public async Task Download_portal_with_download_deny_returns_denied_code_and_reason()
    {
        // El handler evalúa Download deny ANTES del status del archivo — no requiere Available.
        var file = CustomerFile();
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var scope = PortalScopeWith(Rule(AccessResourceType.File, file.Id, AccessCapability.Download, "under review"));

        var result = await IssueDownloadUrlHandler.Handle(
            new IssueDownloadUrlQuery(
                Tenant,
                Actor,
                scope,
                file.Id,
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            files,
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            Options.Create(new TaxVision.CloudStorage.Application.Configuration.CloudStorageOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("cloudstorage.customer_access.denied", result.Error.Code);
        Assert.Equal("under review", result.Error.Message);
    }

    [Fact]
    public async Task Download_portal_with_download_deny_without_reason_uses_default_copy()
    {
        var file = CustomerFile();
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var scope = PortalScopeWith(Rule(AccessResourceType.File, file.Id, AccessCapability.Download));

        var result = await IssueDownloadUrlHandler.Handle(
            new IssueDownloadUrlQuery(
                Tenant,
                Actor,
                scope,
                file.Id,
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            files,
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            Options.Create(new TaxVision.CloudStorage.Application.Configuration.CloudStorageOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("cloudstorage.customer_access.denied", result.Error.Code);
        Assert.Equal(CustomerAccessDenied.Error.Message, result.Error.Message);
    }

    // ------------------------- Effective capabilities -------------------------

    [Fact]
    public void EffectiveCapabilities_portal_flags_denies()
    {
        var file = CustomerFile();
        var scope = PortalScopeWith(
            Rule(AccessResourceType.File, file.Id, AccessCapability.Download),
            Rule(AccessResourceType.File, file.Id, AccessCapability.Delete)
        );

        var caps = EffectiveCapabilities.ForFile(scope, file.Id);

        Assert.False(caps.Download);
        Assert.False(caps.Delete);
        Assert.True(caps.Preview);
        Assert.True(caps.ShareExternal);
    }

    [Fact]
    public void EffectiveCapabilities_staff_is_allow_all()
    {
        var fileId = Guid.NewGuid();
        var scope = new StorageActorScope(false, null);

        var caps = EffectiveCapabilities.ForFile(scope, fileId);

        Assert.Equal(EffectiveCapabilities.AllowAll, caps);
    }

    [Fact]
    public void EffectiveCapabilities_folder_portal_denies_upload()
    {
        var folder = CustomerFolder();
        var scope = PortalScopeWith(Rule(AccessResourceType.Folder, folder.Id, AccessCapability.Upload, "frozen"));

        var caps = EffectiveCapabilities.ForFolder(scope, folder.Id);

        Assert.False(caps.Upload);
        Assert.Equal("frozen", EffectiveCapabilities.ReasonForFolder(scope, folder.Id));
    }

    // ------------------------- Tenant-scope presets (F7) -------------------------

    [Fact]
    public void TenantScope_download_deny_turns_download_off_on_any_file()
    {
        // Sin regla per-item: la regla tenant-scope apaga Download a cualquier recurso del portal.
        var file = CustomerFile();
        var scope = PortalScopeWith(TenantScopeRule(AccessCapability.Download));

        var caps = EffectiveCapabilities.ForFile(scope, file.Id);

        Assert.False(caps.Download);
        Assert.True(caps.Preview);
        Assert.True(caps.Delete);
    }

    [Fact]
    public void TenantScope_download_deny_coexists_with_per_item_deny()
    {
        // No existen allow-overrides: el per-item deny no "revierte" el tenant-scope; ambos suman.
        var file = CustomerFile();
        var scope = PortalScopeWith(
            TenantScopeRule(AccessCapability.Download, "ReadOnly preset"),
            Rule(AccessResourceType.File, file.Id, AccessCapability.Delete, "per-item")
        );

        var caps = EffectiveCapabilities.ForFile(scope, file.Id);

        Assert.False(caps.Download);
        Assert.False(caps.Delete);
    }

    [Fact]
    public void TenantScope_upload_deny_turns_upload_off_on_folder()
    {
        var folder = CustomerFolder();
        var scope = PortalScopeWith(TenantScopeRule(AccessCapability.Upload, "Read-only office-wide"));

        var caps = EffectiveCapabilities.ForFolder(scope, folder.Id);

        Assert.False(caps.Upload);
    }

    [Fact]
    public void TenantScope_visible_deny_hides_everything()
    {
        var file = CustomerFile();
        var scope = PortalScopeWith(TenantScopeRule(AccessCapability.Visible));

        // Preset Visible deny ⇒ IsHiddenForPortal se propaga a todo recurso del portal.
        Assert.True(scope.IsHiddenForPortal(AccessResourceType.File, file.Id));
        Assert.False(scope.CanAccess(file));
    }

    [Fact]
    public void ReasonForFile_prefers_per_item_falls_back_to_tenant_scope()
    {
        var file = CustomerFile();
        var scopeOnlyTenant = PortalScopeWith(TenantScopeRule(AccessCapability.Download, "office preset"));
        Assert.Equal("office preset", EffectiveCapabilities.ReasonForFile(scopeOnlyTenant, file.Id));

        var scopeBoth = PortalScopeWith(
            TenantScopeRule(AccessCapability.Download, "office preset"),
            Rule(AccessResourceType.File, file.Id, AccessCapability.Download, "specific")
        );
        Assert.Equal("specific", EffectiveCapabilities.ReasonForFile(scopeBoth, file.Id));

        var scopeNone = PortalScopeWith();
        Assert.Null(EffectiveCapabilities.ReasonForFile(scopeNone, file.Id));
    }

    [Fact]
    public void ReasonTenantScope_returns_preset_reason_only()
    {
        var scope = PortalScopeWith(TenantScopeRule(AccessCapability.Download, "office preset"));

        Assert.Equal("office preset", EffectiveCapabilities.ReasonTenantScope(scope, AccessCapability.Download));
        Assert.Null(EffectiveCapabilities.ReasonTenantScope(scope, AccessCapability.Upload));
    }

    // ------------------------- Baseline (vista raíz portal) -------------------------

    [Fact]
    public void BaselineCapabilities_portal_with_tenant_scope_upload_deny_is_false()
    {
        // Preset "Preview-only" → TenantScope Upload deny. El portal lee el baseline en la raíz y
        // oculta "Send documents" aunque todavía no haya folder activo.
        var scope = PortalScopeWith(TenantScopeRule(AccessCapability.Upload, "read only"));

        var baseline = EffectiveCapabilities.ForTenantBaseline(scope);

        Assert.NotNull(baseline);
        Assert.False(baseline!.Upload);
        Assert.True(baseline.Preview);
        Assert.True(baseline.Download);
    }

    [Fact]
    public void BaselineCapabilities_portal_without_any_rules_is_null()
    {
        var scope = PortalScopeWith();

        Assert.Null(EffectiveCapabilities.ForTenantBaseline(scope));
    }

    [Fact]
    public void BaselineCapabilities_portal_with_only_per_item_rules_is_null()
    {
        // No hay regla tenant-scope → baseline null (sin ruido en el payload, no es "allow all").
        var file = CustomerFile();
        var scope = PortalScopeWith(Rule(AccessResourceType.File, file.Id, AccessCapability.Download));

        Assert.Null(EffectiveCapabilities.ForTenantBaseline(scope));
    }

    [Fact]
    public void BaselineCapabilities_staff_is_null()
    {
        var scope = new StorageActorScope(false, null);

        Assert.Null(EffectiveCapabilities.ForTenantBaseline(scope));
    }

    [Fact]
    public async Task FolderContents_portal_with_tenant_scope_upload_deny_includes_baseline()
    {
        // Camino del portal (HasAnyPortalDenyRules=true): la respuesta carga el baseline agregado.
        var folders = new FakeFolderRepository();
        var files = new FakeFileObjectRepository();
        var scope = PortalScopeWith(TenantScopeRule(AccessCapability.Upload, "read only"));

        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(Tenant, scope, FolderId: null),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.NotNull(result.CustomerBaselineCapabilities);
        Assert.False(result.CustomerBaselineCapabilities!.Upload);
    }

    [Fact]
    public async Task FolderContents_portal_without_rules_omits_baseline()
    {
        // Portal sin reglas → sin baseline. El payload queda igual que antes del cambio.
        var folders = new FakeFolderRepository();
        var files = new FakeFileObjectRepository();
        var scope = new StorageActorScope(IsCustomerPortal: true, CustomerId: Customer) { TenantId = Tenant };

        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(Tenant, scope, FolderId: null),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.Null(result.CustomerBaselineCapabilities);
    }

    [Fact]
    public async Task FolderContents_staff_omits_baseline()
    {
        var folders = new FakeFolderRepository();
        var files = new FakeFileObjectRepository();
        var scope = new StorageActorScope(false, null);

        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(Tenant, scope, FolderId: null),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.Null(result.CustomerBaselineCapabilities);
    }

    [Fact]
    public async Task FolderContents_portal_with_only_per_item_rules_omits_baseline()
    {
        // Portal con una regla per-item pero sin reglas tenant-scope → baseline null.
        var file = CustomerFile();
        var folders = new FakeFolderRepository();
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var scope = PortalScopeWith(Rule(AccessResourceType.File, file.Id, AccessCapability.Download));

        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(Tenant, scope, FolderId: null),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.Null(result.CustomerBaselineCapabilities);
    }

    // ------------------------- Multipart initiate handler -------------------------

    [Fact]
    public async Task Multipart_initiate_portal_with_upload_deny_on_destination_returns_denied_code_and_reason()
    {
        // Mismo contrato que el single-shot: sin este gate el multipart es un bypass real
        // del Upload deny per-folder cuando PerItemAccess:Enabled=true.
        var folder = CustomerFolder(category: "sys.documents");
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var files = new FakeFileObjectRepository();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(Tenant, "starter", maxBytes: 100_000_000, maxFileSizeBytes: 100_000_000));
        var scope = PortalScopeWith(
            Rule(AccessResourceType.Folder, folder.Id, AccessCapability.Upload, "folder locked")
        );

        var result = await InitiateMultipartUploadHandler.Handle(
            new InitiateMultipartUploadCommand(
                Tenant,
                Actor,
                scope,
                new InitiateMultipartUploadRequest(
                    "report.pdf",
                    "application/pdf",
                    12L * 1024 * 1024,
                    OwnerType.Customer,
                    Customer,
                    FolderType.Documents,
                    2025
                ),
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            files,
            folders,
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            new FakeMultipartUploadStorage(),
            Options.Create(new CloudStorageOptions { MultipartPartSizeBytes = 5L * 1024 * 1024 }),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("cloudstorage.customer_access.denied", result.Error.Code);
        Assert.Equal("folder locked", result.Error.Message);
    }

    [Fact]
    public async Task Multipart_initiate_portal_without_upload_deny_on_destination_succeeds()
    {
        // Control positivo: con el mismo setup pero sin la regla de Upload deny, el handler
        // registra el archivo y devuelve las URLs presignadas por parte.
        var folder = CustomerFolder(category: "sys.documents");
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var files = new FakeFileObjectRepository();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(Tenant, "starter", maxBytes: 100_000_000, maxFileSizeBytes: 100_000_000));
        // Scope portal con reglas (HasAnyPortalDenyRules=true), pero el deny es Download y no Upload.
        var scope = PortalScopeWith(Rule(AccessResourceType.Folder, folder.Id, AccessCapability.Download));

        var result = await InitiateMultipartUploadHandler.Handle(
            new InitiateMultipartUploadCommand(
                Tenant,
                Actor,
                scope,
                new InitiateMultipartUploadRequest(
                    "report.pdf",
                    "application/pdf",
                    12L * 1024 * 1024,
                    OwnerType.Customer,
                    Customer,
                    FolderType.Documents,
                    2025
                ),
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            files,
            folders,
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            new FakeMultipartUploadStorage(),
            Options.Create(new CloudStorageOptions { MultipartPartSizeBytes = 5L * 1024 * 1024 }),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Parts.Count);
    }

    private static StorageActorScopeResolver NewResolver(FakeCustomerAccessRuleRepository rules, bool perItemEnabled)
    {
        var assignments = new FakeCustomerAssignmentProjectionStore();
        var permissions = new NeverHasPermissionSource();
        var visibility = Options.Create(new CloudStorageVisibilityOptions { Enabled = false });
        var perItem = Options.Create(new CloudStoragePerItemAccessOptions { Enabled = perItemEnabled });
        return new StorageActorScopeResolver(assignments, permissions, visibility, perItem, rules);
    }

    private static ClaimsPrincipal NewPortalPrincipal() =>
        new(new ClaimsIdentity([new Claim("actor_type", "CustomerPortal")], "test"));

    private static ClaimsPrincipal NewStaffPrincipal() =>
        new(new ClaimsIdentity([new Claim("actor_type", "TenantEmployee")], "test"));

    private sealed class NeverHasPermissionSource : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
