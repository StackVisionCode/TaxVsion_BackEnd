using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>D-33: el tamaño sale de la cadena en BD (Tenant > Plan > Global) y cae a CloudStorageOptions sin filas.</summary>
public sealed class UploadPolicyResolverTests
{
    private const long MB = 1024L * 1024;
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static CloudStorageOptions OptionsWithStarterRecordings150MB()
    {
        var options = new CloudStorageOptions();
        options.PlanPolicies["starter"] = new StoragePlanPolicy
        {
            MaxFileSizeBytes = 10 * MB,
            AllowedExtensions = [".pdf", ".webm", ".mp4"],
            AllowedContentTypes = ["application/pdf", "video/webm", "video/mp4"],
            FolderOverridesBytes = new(StringComparer.OrdinalIgnoreCase) { ["Recordings"] = 150 * MB },
        };
        return options;
    }

    private static UploadPolicyResolver Resolver(
        FakeUploadSizePolicyRepository repository,
        CloudStorageOptions? options = null,
        UploadPolicyCache? cache = null,
        DateTime? now = null
    ) =>
        new(
            repository,
            Options.Create(options ?? OptionsWithStarterRecordings150MB()),
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
    public async Task Without_rows_the_size_is_the_CloudStorageOptions_fallback()
    {
        var repository = new FakeUploadSizePolicyRepository();
        var options = OptionsWithStarterRecordings150MB();

        var policy = await Resolver(repository, options)
            .ResolveAsync(Tenant, "starter", FolderType.Recordings, default);

        Assert.Equal(
            options.ResolveUploadPolicy("starter", FolderType.Recordings).MaxFileSizeBytes,
            policy.MaxFileSizeBytes
        );
        Assert.Equal(150 * MB, policy.MaxFileSizeBytes);
    }

    [Fact]
    public async Task A_Global_row_overrides_the_fallback_even_above_the_FolderType_500MB_code_ceiling()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 2048 * MB));

        var policy = await Resolver(repository).ResolveAsync(Tenant, "starter", FolderType.Recordings, default);

        // Antes RecordingsPolicy() (500 MB) era el tope duro; ahora el único techo es el técnico (5 GB).
        Assert.Equal(2048 * MB, policy.MaxFileSizeBytes);
    }

    [Fact]
    public async Task Plan_row_beats_Global_and_Tenant_row_beats_Plan()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 1000 * MB));
        repository.Seed(Plan("starter", FolderType.Recordings, 700 * MB));
        var resolver = Resolver(repository);

        var withPlan = await resolver.ResolveAsync(Tenant, "starter", FolderType.Recordings, default);
        Assert.Equal(700 * MB, withPlan.MaxFileSizeBytes);

        repository.Seed(TenantRow(Tenant, FolderType.Recordings, 300 * MB));
        var withTenant = await Resolver(repository).ResolveAsync(Tenant, "starter", FolderType.Recordings, default);
        Assert.Equal(300 * MB, withTenant.MaxFileSizeBytes);

        // Otro tenant del mismo plan no ve la fila Tenant; otro plan no ve la fila Plan.
        var otherTenant = await Resolver(repository)
            .ResolveAsync(Guid.NewGuid(), "starter", FolderType.Recordings, default);
        Assert.Equal(700 * MB, otherTenant.MaxFileSizeBytes);
        var otherPlan = await Resolver(repository)
            .ResolveAsync(Guid.NewGuid(), "enterprise", FolderType.Recordings, default);
        Assert.Equal(1000 * MB, otherPlan.MaxFileSizeBytes);
    }

    [Fact]
    public async Task Rows_of_another_FolderType_do_not_apply()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 2048 * MB));

        var documents = await Resolver(repository).ResolveAsync(Tenant, "starter", FolderType.Documents, default);

        Assert.Equal(10 * MB, documents.MaxFileSizeBytes);
    }

    [Fact]
    public async Task Overrides_never_touch_the_allowed_types()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 2048 * MB));
        var options = OptionsWithStarterRecordings150MB();

        var policy = await Resolver(repository, options)
            .ResolveAsync(Tenant, "starter", FolderType.Recordings, default);
        var fromCode = options.ResolveUploadPolicy("starter", FolderType.Recordings);

        Assert.Equal(fromCode.AllowedExtensions, policy.AllowedExtensions);
        Assert.Equal(fromCode.AllowedContentTypes, policy.AllowedContentTypes);
    }

    [Fact]
    public async Task Resolved_size_is_cached_for_60_seconds_and_dropped_on_Invalidate()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Global(FolderType.Recordings, 1000 * MB));
        var cache = new UploadPolicyCache();
        var resolver = Resolver(repository, cache: cache);

        var first = await resolver.ResolveAsync(Tenant, "starter", FolderType.Recordings, default);
        repository.Seed(TenantRow(Tenant, FolderType.Recordings, 300 * MB));
        var cached = await resolver.ResolveAsync(Tenant, "starter", FolderType.Recordings, default);

        Assert.Equal(1000 * MB, first.MaxFileSizeBytes);
        Assert.Equal(1000 * MB, cached.MaxFileSizeBytes);
        Assert.Equal(1, repository.ApplicableReads);

        cache.Invalidate();
        var fresh = await resolver.ResolveAsync(Tenant, "starter", FolderType.Recordings, default);
        Assert.Equal(300 * MB, fresh.MaxFileSizeBytes);
        Assert.Equal(2, repository.ApplicableReads);

        // Pasado el TTL vuelve a leer aunque nadie invalide.
        var later = Resolver(
            repository,
            cache: cache,
            now: Now + UploadPolicyCache.TimeToLive + TimeSpan.FromSeconds(1)
        );
        await later.ResolveAsync(Tenant, "starter", FolderType.Recordings, default);
        Assert.Equal(3, repository.ApplicableReads);
    }

    [Fact]
    public async Task Cache_key_distinguishes_folder_plan_and_tenant()
    {
        var repository = new FakeUploadSizePolicyRepository();
        repository.Seed(Plan("starter", FolderType.Recordings, 700 * MB));
        repository.Seed(Plan("enterprise", FolderType.Recordings, 900 * MB));
        var resolver = Resolver(repository, cache: new UploadPolicyCache());

        var starter = await resolver.ResolveAsync(Tenant, "starter", FolderType.Recordings, default);
        var enterprise = await resolver.ResolveAsync(Tenant, "enterprise", FolderType.Recordings, default);
        var starterAgain = await resolver.ResolveAsync(Tenant, "STARTER", FolderType.Recordings, default);

        Assert.Equal(700 * MB, starter.MaxFileSizeBytes);
        Assert.Equal(900 * MB, enterprise.MaxFileSizeBytes);
        Assert.Equal(700 * MB, starterAgain.MaxFileSizeBytes);
        Assert.Equal(2, repository.ApplicableReads);
    }
}
