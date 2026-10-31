using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class UploadSizePolicyHandlerTests
{
    private const long MB = 1024L * 1024;
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Fixture
    {
        public FakeUploadSizePolicyRepository Policies { get; } = new();
        public FakeUploadSizePolicyAuditRepository Audit { get; } = new();
        public UploadPolicyCache Cache { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();

        public Task<BuildingBlocks.Results.Result<SetUploadSizePolicyResponse>> SetAsync(
            UploadSizePolicyScope scope,
            FolderType folderType,
            long bytes,
            string? planCode = null,
            Guid? tenantId = null
        ) =>
            SetUploadSizePolicyHandler.Handle(
                new SetUploadSizePolicyCommand(scope, folderType, planCode, tenantId, bytes, Actor),
                Policies,
                Audit,
                Cache,
                new FakeSystemClock(Now),
                UnitOfWork,
                CancellationToken.None
            );

        public Task<BuildingBlocks.Results.Result<RemoveUploadSizePolicyResponse>> RemoveAsync(
            UploadSizePolicyScope scope,
            FolderType folderType,
            string? planCode = null,
            Guid? tenantId = null
        ) =>
            RemoveUploadSizePolicyHandler.Handle(
                new RemoveUploadSizePolicyCommand(scope, folderType, planCode, tenantId, Actor),
                Policies,
                Audit,
                Cache,
                new FakeSystemClock(Now),
                UnitOfWork,
                CancellationToken.None
            );

        /// <summary>Carga la caché con lo que haya ahora para comprobar que una escritura la vacía.</summary>
        public async Task<long> WarmCacheAsync(FolderType folderType, string planCode, Guid tenantId)
        {
            var resolver = new UploadPolicyResolver(
                Policies,
                Options.Create(new CloudStorageOptions()),
                Cache,
                new FakeSystemClock(Now)
            );
            return (
                await resolver.ResolveAsync(tenantId, planCode, folderType, CancellationToken.None)
            ).MaxFileSizeBytes;
        }
    }

    [Fact]
    public async Task Set_creates_the_row_audits_old_null_and_invalidates_the_cache()
    {
        var fixture = new Fixture();
        var before = await fixture.WarmCacheAsync(FolderType.Recordings, "starter", Tenant);

        var result = await fixture.SetAsync(UploadSizePolicyScope.Global, FolderType.Recordings, 2048 * MB);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        Assert.Equal("Global", result.Value.Policy.Scope);
        Assert.Equal(2048 * MB, result.Value.Policy.MaxFileSizeBytes);
        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Null(entry.OldMaxFileSizeBytes);
        Assert.Equal(2048 * MB, entry.NewMaxFileSizeBytes);
        Assert.Equal(Actor, entry.ActorUserId);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
        // La caché quedó invalidada: la siguiente resolución ya ve la fila nueva.
        Assert.NotEqual(before, await fixture.WarmCacheAsync(FolderType.Recordings, "starter", Tenant));
        Assert.Equal(2048 * MB, await fixture.WarmCacheAsync(FolderType.Recordings, "starter", Tenant));
    }

    [Fact]
    public async Task Set_on_an_existing_row_updates_it_and_audits_old_and_new()
    {
        var fixture = new Fixture();
        await fixture.SetAsync(UploadSizePolicyScope.Plan, FolderType.Recordings, 700 * MB, planCode: "starter");

        var result = await fixture.SetAsync(
            UploadSizePolicyScope.Plan,
            FolderType.Recordings,
            900 * MB,
            planCode: "starter"
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Created);
        Assert.Single(fixture.Policies.All());
        Assert.Equal(2, fixture.Audit.Entries.Count);
        Assert.Equal(700 * MB, fixture.Audit.Entries[1].OldMaxFileSizeBytes);
        Assert.Equal(900 * MB, fixture.Audit.Entries[1].NewMaxFileSizeBytes);
        Assert.Equal("starter", fixture.Audit.Entries[1].PlanCode);
    }

    [Fact]
    public async Task Set_above_the_technical_ceiling_fails_with_OutOfRange_without_persisting()
    {
        var fixture = new Fixture();

        var result = await fixture.SetAsync(
            UploadSizePolicyScope.Tenant,
            FolderType.Recordings,
            UploadSizePolicy.TechnicalMaxBytes + 1,
            tenantId: Tenant
        );

        Assert.True(result.IsFailure);
        Assert.Equal(UploadSizePolicyErrors.OutOfRange, result.Error);
        Assert.Empty(fixture.Policies.All());
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Set_Plan_without_plan_code_and_Tenant_without_tenant_are_rejected()
    {
        var fixture = new Fixture();

        var plan = await fixture.SetAsync(UploadSizePolicyScope.Plan, FolderType.Recordings, 10 * MB);
        var tenant = await fixture.SetAsync(UploadSizePolicyScope.Tenant, FolderType.Recordings, 10 * MB);

        Assert.Equal(UploadSizePolicyErrors.InvalidPlanCode, plan.Error);
        Assert.Equal(UploadSizePolicyErrors.InvalidTenant, tenant.Error);
        Assert.Empty(fixture.Policies.All());
    }

    [Fact]
    public async Task Remove_existing_row_deletes_it_and_audits_new_null()
    {
        var fixture = new Fixture();
        // 400 MB a propósito: el fallback de código para Recordings es 300 MB y el test debe distinguirlos.
        await fixture.SetAsync(UploadSizePolicyScope.Tenant, FolderType.Recordings, 400 * MB, tenantId: Tenant);
        Assert.Equal(400 * MB, await fixture.WarmCacheAsync(FolderType.Recordings, "starter", Tenant));

        var result = await fixture.RemoveAsync(UploadSizePolicyScope.Tenant, FolderType.Recordings, tenantId: Tenant);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Removed);
        Assert.Empty(fixture.Policies.All());
        var entry = fixture.Audit.Entries[^1];
        Assert.Equal(400 * MB, entry.OldMaxFileSizeBytes);
        Assert.Null(entry.NewMaxFileSizeBytes);
        Assert.Equal(Tenant, entry.TenantId);
        Assert.Equal(2, fixture.UnitOfWork.SaveChangesCallCount);
        Assert.Equal(300 * MB, await fixture.WarmCacheAsync(FolderType.Recordings, "starter", Tenant));
    }

    [Fact]
    public async Task Remove_nonexistent_row_is_idempotent_without_audit_or_save()
    {
        var fixture = new Fixture();

        var result = await fixture.RemoveAsync(UploadSizePolicyScope.Plan, FolderType.Recordings, planCode: "starter");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Removed);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task List_returns_rows_with_the_technical_ceiling()
    {
        var fixture = new Fixture();
        await fixture.SetAsync(UploadSizePolicyScope.Global, FolderType.Recordings, 2048 * MB);

        var response = await ListUploadSizePoliciesHandler.Handle(
            new ListUploadSizePoliciesQuery(),
            fixture.Policies,
            CancellationToken.None
        );

        Assert.Equal(UploadSizePolicy.TechnicalMaxBytes, response.TechnicalMaxBytes);
        var row = Assert.Single(response.Policies);
        Assert.Equal("Recordings", row.FolderType);
        Assert.Equal(2048 * MB, row.MaxFileSizeBytes);
    }
}
