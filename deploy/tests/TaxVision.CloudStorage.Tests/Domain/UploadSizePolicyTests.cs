using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Domain;

public sealed class UploadSizePolicyTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Technical_ceiling_is_5_GB()
    {
        Assert.Equal(5L * 1024 * 1024 * 1024, UploadSizePolicy.TechnicalMaxBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(UploadSizePolicy.TechnicalMaxBytes + 1)]
    public void Create_rejects_sizes_outside_1_byte_to_the_technical_ceiling(long bytes)
    {
        var result = UploadSizePolicy.CreateGlobal(FolderType.Recordings, bytes, Actor, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadSizePolicyErrors.OutOfRange, result.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2147483648L)]
    [InlineData(UploadSizePolicy.TechnicalMaxBytes)]
    public void Create_accepts_sizes_within_range_including_the_ceiling_itself(long bytes)
    {
        var result = UploadSizePolicy.CreateGlobal(FolderType.Recordings, bytes, Actor, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(bytes, result.Value.MaxFileSizeBytes);
        Assert.Equal(UploadSizePolicyScope.Global, result.Value.Scope);
        Assert.Null(result.Value.PlanCode);
        Assert.Null(result.Value.TenantId);
    }

    [Fact]
    public void SetMaxFileSizeBytes_above_the_ceiling_fails_and_keeps_the_previous_value()
    {
        var policy = UploadSizePolicy.CreateGlobal(FolderType.Recordings, 100, Actor, Now).Value;

        var result = policy.SetMaxFileSizeBytes(
            UploadSizePolicy.TechnicalMaxBytes + 1,
            Guid.NewGuid(),
            Now.AddMinutes(1)
        );

        Assert.True(result.IsFailure);
        Assert.Equal(UploadSizePolicyErrors.OutOfRange, result.Error);
        Assert.Equal(100, policy.MaxFileSizeBytes);
        Assert.Equal(Actor, policy.UpdatedByUserId);
        Assert.Equal(Now, policy.UpdatedAtUtc);
    }

    [Fact]
    public void SetMaxFileSizeBytes_within_range_records_actor_and_time()
    {
        var policy = UploadSizePolicy.CreateGlobal(FolderType.Recordings, 100, Actor, Now).Value;
        var other = Guid.NewGuid();

        var result = policy.SetMaxFileSizeBytes(200, other, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(200, policy.MaxFileSizeBytes);
        Assert.Equal(other, policy.UpdatedByUserId);
        Assert.Equal(Now.AddMinutes(1), policy.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePlan_requires_a_plan_code(string? planCode)
    {
        var result = UploadSizePolicy.CreatePlan(planCode!, FolderType.Recordings, 100, Actor, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadSizePolicyErrors.InvalidPlanCode, result.Error);
    }

    [Fact]
    public void CreatePlan_trims_the_plan_code()
    {
        var result = UploadSizePolicy.CreatePlan("  enterprise ", FolderType.Recordings, 100, Actor, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("enterprise", result.Value.PlanCode);
        Assert.Equal(UploadSizePolicyScope.Plan, result.Value.Scope);
    }

    [Fact]
    public void CreateTenant_requires_a_tenant_id()
    {
        var result = UploadSizePolicy.CreateTenant(Guid.Empty, FolderType.Recordings, 100, Actor, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadSizePolicyErrors.InvalidTenant, result.Error);
    }

    [Fact]
    public void Create_rejects_an_undefined_folder_type()
    {
        var result = UploadSizePolicy.CreateGlobal((FolderType)999, 100, Actor, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadSizePolicyErrors.InvalidFolderType, result.Error);
    }
}
