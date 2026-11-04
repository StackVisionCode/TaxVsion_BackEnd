using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// El resolutor junta catálogo y carpeta (BD de plataforma), lo que vende el plan (proyección del tenant), la
/// excepción de tamaño en BD (Tenant > Plan > Global, D-33) y, para el portal, la restricción de la oficina.
/// </summary>
public sealed class UploadPolicyResolverTests
{
    private const long MB = 1024L * 1024;
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static TenantStorageLimit Limit(
        Guid tenantId,
        string planCode = "starter",
        long maxFileBytes = 10 * MB,
        string? groups = null
    ) => TenantStorageLimit.Create(tenantId, planCode, 10L * 1024 * MB, maxFileBytes, groups);

    private static UploadPolicyResolver Resolver(
        FakeUploadSizePolicyRepository repository,
        UploadPolicyCache? cache = null,
        DateTime? now = null,
        InMemoryFilePolicyCatalog? catalog = null,
        FilePolicyCatalogCache? catalogCache = null
    ) =>
        new(
            repository,
            catalog ?? new InMemoryFilePolicyCatalog(),
            catalogCache ?? new FilePolicyCatalogCache(),
            cache ?? new UploadPolicyCache(),
            new FakeSystemClock(now ?? Now)
        );

    private static UploadSizePolicy Global(FolderType folderType, long bytes) =>
        UploadSizePolicy.CreateGlobal(folderType, bytes, Actor, Now).Value;

    private static UploadSizePolicy Plan(string planCode, FolderType folderType, long bytes) =>
        UploadSizePolicy.CreatePlan(planCode, folderType, bytes, Actor, Now).Value;

    private static UploadSizePolicy TenantRow(Guid tenantId, FolderType folderType, long bytes) =>
        UploadSizePolicy.CreateTenant(tenantId, folderType, bytes, Actor, Now).Value;

    [Fact]
    public async Task Without_rows_documents_get_the_smaller_of_folder_and_plan()
    {
        var policy = await Resolver(new FakeUploadSizePolicyRepository())
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Documents, false, default);

        Assert.Equal(10 * MB, policy.MaxFileSizeBytes);
    }

    [Fact]
    public async Task A_Global_row_overrides_the_folder_even_above_its_500MB_ceiling()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 2048 * MB));

        var policy = await Resolver(repository)
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default);

        Assert.Equal(2048 * MB, policy.MaxFileSizeBytes);
    }

    [Fact]
    public async Task Plan_row_beats_Global_and_Tenant_row_beats_Plan()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 1000 * MB));
        repository.Seed(Plan("starter", FolderType.Recordings, 700 * MB));

        var withPlan = await Resolver(repository)
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default);
        Assert.Equal(700 * MB, withPlan.MaxFileSizeBytes);

        repository.Seed(TenantRow(Tenant, FolderType.Recordings, 300 * MB));
        var withTenant = await Resolver(repository)
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default);
        Assert.Equal(300 * MB, withTenant.MaxFileSizeBytes);

        var other = Guid.NewGuid();
        Assert.Equal(
            700 * MB,
            (
                await Resolver(repository).ResolveAsync(other, Limit(other), FolderType.Recordings, false, default)
            ).MaxFileSizeBytes
        );
        Assert.Equal(
            1000 * MB,
            (
                await Resolver(repository)
                    .ResolveAsync(other, Limit(other, "enterprise"), FolderType.Recordings, false, default)
            ).MaxFileSizeBytes
        );
    }

    [Fact]
    public async Task Rows_of_another_folder_do_not_apply()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 2048 * MB));

        var documents = await Resolver(repository)
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Documents, false, default);

        Assert.Equal(10 * MB, documents.MaxFileSizeBytes);
    }

    [Fact]
    public async Task Size_overrides_never_touch_the_allowed_types()
    {
        var repository = new FakeUploadSizePolicyRepository();
        var before = await Resolver(repository)
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default);
        repository.Seed(Global(FolderType.Recordings, 2048 * MB));

        var after = await Resolver(repository)
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default);

        Assert.Equal(before.AllowedExtensions, after.AllowedExtensions);
        Assert.Equal(before.AllowedContentTypes, after.AllowedContentTypes);
    }

    [Fact]
    public async Task The_plan_groups_come_from_the_tenant_projection()
    {
        var starter = await Resolver(new FakeUploadSizePolicyRepository())
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Documents, false, default);
        var pro = await Resolver(new FakeUploadSizePolicyRepository())
            .ResolveAsync(
                Tenant,
                Limit(
                    Tenant,
                    "pro",
                    25 * MB,
                    "documents,spreadsheets,images,data,media,presentations,rich_text,web_images,archives,xml"
                ),
                FolderType.Documents,
                false,
                default
            );

        Assert.DoesNotContain(".zip", starter.AllowedExtensions);
        Assert.Contains(".zip", pro.AllowedExtensions);
        Assert.Equal(25 * MB, pro.MaxFileSizeBytes);
    }

    [Fact]
    public async Task The_portal_restriction_applies_only_to_customer_uploads()
    {
        var limit = Limit(Tenant);
        Assert.True(limit.SetPortalUploadRestriction(["documents"], 2 * MB).IsSuccess);
        var resolver = Resolver(new FakeUploadSizePolicyRepository());

        var staff = await resolver.ResolveAsync(Tenant, limit, FolderType.Documents, forCustomerPortal: false, default);
        var portal = await resolver.ResolveAsync(Tenant, limit, FolderType.Documents, forCustomerPortal: true, default);

        Assert.Contains(".png", staff.AllowedExtensions);
        Assert.DoesNotContain(".png", portal.AllowedExtensions);
        Assert.Equal(2 * MB, portal.MaxFileSizeBytes);
    }

    [Fact]
    public void The_office_cannot_widen_beyond_its_plan()
    {
        var limit = Limit(Tenant);

        Assert.True(limit.SetPortalUploadRestriction(["archives"], null).IsFailure);
        Assert.True(limit.SetPortalUploadRestriction(null, 100).IsFailure);
    }

    [Fact]
    public async Task A_folder_without_its_own_policy_falls_back_to_Other()
    {
        var catalog = new InMemoryFilePolicyCatalog(without: FolderType.Backups);

        var backups = await Resolver(new FakeUploadSizePolicyRepository(), catalog: catalog)
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Backups, false, default);
        var other = await Resolver(new FakeUploadSizePolicyRepository())
            .ResolveAsync(Tenant, Limit(Tenant), FolderType.Other, false, default);

        Assert.Equal(other.AllowedExtensions, backups.AllowedExtensions);
    }

    [Fact]
    public async Task The_size_override_is_cached_for_60_seconds_and_dropped_on_Invalidate()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 1000 * MB));
        var cache = new UploadPolicyCache();
        var resolver = Resolver(repository, cache: cache);

        var first = await resolver.ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default);
        repository.Seed(TenantRow(Tenant, FolderType.Recordings, 300 * MB));
        var cached = await resolver.ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default);

        Assert.Equal(1000 * MB, first.MaxFileSizeBytes);
        Assert.Equal(1000 * MB, cached.MaxFileSizeBytes);
        Assert.Equal(1, repository.ApplicableReads);

        cache.Invalidate();
        Assert.Equal(
            300 * MB,
            (await resolver.ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default)).MaxFileSizeBytes
        );
        Assert.Equal(2, repository.ApplicableReads);

        var later = Resolver(
            repository,
            cache: cache,
            now: Now + UploadPolicyCache.TimeToLive + TimeSpan.FromSeconds(1)
        );
        await later.ResolveAsync(Tenant, Limit(Tenant), FolderType.Recordings, false, default);
        Assert.Equal(3, repository.ApplicableReads);
    }

    [Fact]
    public async Task The_catalog_is_read_once_per_ttl_and_again_after_Invalidate()
    {
        var catalog = new InMemoryFilePolicyCatalog();
        var catalogCache = new FilePolicyCatalogCache();
        var resolver = Resolver(new FakeUploadSizePolicyRepository(), catalog: catalog, catalogCache: catalogCache);

        await resolver.ResolveAsync(Tenant, Limit(Tenant), FolderType.Documents, false, default);
        await resolver.ResolveAsync(Tenant, Limit(Tenant), FolderType.Tasks, false, default);
        Assert.Equal(1, catalog.Reads);

        catalogCache.Invalidate();
        await resolver.ResolveAsync(Tenant, Limit(Tenant), FolderType.Documents, false, default);
        Assert.Equal(2, catalog.Reads);
    }

    internal sealed class InMemoryFilePolicyCatalog(FolderType? without = null) : IFilePolicyCatalogRepository
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<FileTypeDefinition>> ListFileTypesAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(TestFilePolicyCatalog.FileTypes());
        }

        public Task<FileTypeDefinition?> GetFileTypeAsync(string extension, CancellationToken ct) =>
            Task.FromResult(
                TestFilePolicyCatalog.FileTypes().SingleOrDefault(definition => definition.Extension == extension)
            );

        public void AddFileType(FileTypeDefinition definition) { }

        public Task<IReadOnlyList<FolderFilePolicy>> ListFolderPoliciesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FolderFilePolicy>>(
                TestFilePolicyCatalog.FolderPolicies().Values.Where(policy => policy.FolderType != without).ToList()
            );

        public Task<FolderFilePolicy?> GetFolderPolicyAsync(FolderType folderType, CancellationToken ct) =>
            Task.FromResult(TestFilePolicyCatalog.FolderPolicies().GetValueOrDefault(folderType));

        public void AddAudit(FilePolicyAudit audit) { }
    }
}
