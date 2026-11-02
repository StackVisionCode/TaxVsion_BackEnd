using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// El plan llega por entitlements (grupos y tamaño), la oficina acota lo del portal y PlatformAdmin edita
/// catálogo y carpetas sin redeploy, siempre dentro de los invariantes (lista negra, catálogo existente).
/// </summary>
public sealed class FilePolicyHandlersTests
{
    private const long Mb = 1024 * 1024;
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task The_plan_file_groups_and_size_are_projected_from_entitlements()
    {
        var limits = new FakeStorageLimitRepository();

        await Project(limits, new Dictionary<string, string>
        {
            ["storage.max_bytes"] = (50L * 1024 * Mb).ToString(),
            ["storage.max_file_bytes"] = (25 * Mb).ToString(),
            ["storage.file_type_groups"] = "documents,archives",
        });

        var allowance = (await limits.GetAsync(Tenant, default))!.PlanAllowance();
        Assert.Equal(25 * Mb, allowance.MaxFileBytes);
        Assert.Equal(["archives", "documents"], allowance.FileTypeGroups.Order());
    }

    [Fact]
    public async Task A_plan_without_the_new_keys_gets_the_smallest_tier_not_the_widest()
    {
        var limits = new FakeStorageLimitRepository();

        await Project(limits, new Dictionary<string, string> { ["storage.max_bytes"] = "1000" });

        var allowance = (await limits.GetAsync(Tenant, default))!.PlanAllowance();
        Assert.Equal(PlanFileAllowanceDefaults.MaxFileBytes, allowance.MaxFileBytes);
        Assert.DoesNotContain("archives", allowance.FileTypeGroups);
    }

    [Fact]
    public async Task The_office_settings_show_the_plan_ceiling_and_save_a_narrower_restriction()
    {
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(Tenant, "pro", 1000 * Mb, 25 * Mb, "documents,images,archives"));

        var saved = await PortalUploadSettingsHandler.Handle(
            new UpdatePortalUploadSettingsCommand(Tenant, ["documents"], 5 * Mb),
            limits,
            new FakeUnitOfWork(),
            default
        );
        var rejected = await PortalUploadSettingsHandler.Handle(
            new UpdatePortalUploadSettingsCommand(Tenant, ["presentations"], null),
            limits,
            new FakeUnitOfWork(),
            default
        );

        Assert.Equal(["archives", "documents", "images"], saved.Value.PlanFileTypeGroups);
        Assert.Equal(["documents"], saved.Value.PortalFileTypeGroups);
        Assert.Equal(5 * Mb, saved.Value.PortalMaxFileBytes);
        Assert.Equal(QuotaErrors.InvalidPortalUploadRestriction, rejected.Error);
    }

    [Fact]
    public async Task Adding_a_file_type_is_audited_and_refreshes_the_cache()
    {
        var catalog = new RecordingCatalog();
        var cache = new FilePolicyCatalogCache();
        cache.Set(new FilePolicyCatalog([], new Dictionary<FolderType, FolderFilePolicy>()), DateTime.UtcNow);

        var result = await FilePolicyCatalogHandler.Handle(
            new UpsertFileTypeCommand("HEIC", "images", ["image/heic"], true, Guid.NewGuid()),
            catalog,
            cache,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(".heic", result.Value.Extension);
        Assert.Single(catalog.Added);
        Assert.Equal("file_type", Assert.Single(catalog.Audits).Kind);
        Assert.False(cache.TryGet(DateTime.UtcNow, out _));
    }

    [Fact]
    public async Task A_dangerous_extension_cannot_be_added_to_the_catalog()
    {
        var result = await FilePolicyCatalogHandler.Handle(
            new UpsertFileTypeCommand(".exe", "documents", ["application/x-msdownload"], true, Guid.NewGuid()),
            new RecordingCatalog(),
            new FilePolicyCatalogCache(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            default
        );

        Assert.Equal(FileTypeErrors.DangerousExtension, result.Error);
    }

    [Fact]
    public async Task A_folder_can_only_list_extensions_that_exist_in_the_catalog()
    {
        var result = await FilePolicyCatalogHandler.Handle(
            new UpdateFolderPolicyCommand(FolderType.Documents, 25 * Mb, [".pdf", ".heic"], true, true, Guid.NewGuid()),
            new RecordingCatalog(),
            new FilePolicyCatalogCache(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            default
        );

        Assert.Equal(FileTypeErrors.UnknownExtension, result.Error);
    }

    private static Task Project(FakeStorageLimitRepository limits, IReadOnlyDictionary<string, string> values) =>
        TenantEntitlementsChangedQuotaConsumer.Handle(
            new TenantEntitlementsChangedIntegrationEvent
            {
                TenantId = Tenant,
                RevisionNumber = 1,
                ChangedKeys = [],
                PlanCode = "pro",
                SubscriptionStatus = "Active",
                SeatCount = 1,
                AvailableSeatCount = 1,
                EntitlementValues = values,
            },
            limits,
            Options.Create(new CloudStorageOptions()),
            new FakeUnitOfWork(),
            new NoCorrelation(),
            default
        );

    private sealed class RecordingCatalog : IFilePolicyCatalogRepository
    {
        public List<FileTypeDefinition> Added { get; } = [];
        public List<FilePolicyAudit> Audits { get; } = [];

        public Task<IReadOnlyList<FileTypeDefinition>> ListFileTypesAsync(CancellationToken ct) =>
            Task.FromResult(TestFilePolicyCatalog.FileTypes());

        public Task<FileTypeDefinition?> GetFileTypeAsync(string extension, CancellationToken ct) =>
            Task.FromResult(TestFilePolicyCatalog.FileTypes().SingleOrDefault(definition => definition.Extension == extension));

        public void AddFileType(FileTypeDefinition definition) => Added.Add(definition);

        public Task<IReadOnlyList<FolderFilePolicy>> ListFolderPoliciesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FolderFilePolicy>>(TestFilePolicyCatalog.FolderPolicies().Values.ToList());

        public Task<FolderFilePolicy?> GetFolderPolicyAsync(FolderType folderType, CancellationToken ct) =>
            Task.FromResult(TestFilePolicyCatalog.FolderPolicies().GetValueOrDefault(folderType));

        public void AddAudit(FilePolicyAudit audit) => Audits.Add(audit);
    }

    private sealed class NoCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
