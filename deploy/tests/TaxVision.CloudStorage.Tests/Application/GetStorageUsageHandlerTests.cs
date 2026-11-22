using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Administration;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>El desglose por categoría y la papelera solo se leen cuando se piden: la página Documents usa el uso a secas.</summary>
public sealed class GetStorageUsageHandlerTests
{
    private readonly Guid _tenant = Guid.NewGuid();

    [Fact]
    public async Task Breakdown_only_when_requested()
    {
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(_tenant, "enterprise", 1_000, 100));
        var reader = new CountingBreakdownReader(
            new StorageBreakdown([new CategoryUsage("Documents", 2, 30)], new TrashUsage(1, 5))
        );

        var plain = await GetStorageUsageHandler.Handle(new GetStorageUsageQuery(_tenant), limits, reader, default);
        var detailed = await GetStorageUsageHandler.Handle(
            new GetStorageUsageQuery(_tenant, IncludeBreakdown: true),
            limits,
            reader,
            default
        );

        Assert.Null(plain.Value.ByCategory);
        Assert.Null(plain.Value.Trash);
        Assert.Equal([new CategoryUsage("Documents", 2, 30)], detailed.Value.ByCategory!);
        Assert.Equal(new TrashUsage(1, 5), detailed.Value.Trash);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task Without_a_quota_row_it_is_not_provisioned_and_reads_nothing()
    {
        var reader = new CountingBreakdownReader(new StorageBreakdown([], new TrashUsage(0, 0)));

        var result = await GetStorageUsageHandler.Handle(
            new GetStorageUsageQuery(_tenant, IncludeBreakdown: true),
            new FakeStorageLimitRepository(),
            reader,
            default
        );

        Assert.Equal(QuotaErrors.NotProvisioned, result.Error);
        Assert.Equal(0, reader.Calls);
    }

    private sealed class CountingBreakdownReader(StorageBreakdown breakdown) : IStorageBreakdownReader
    {
        public int Calls { get; private set; }

        public Task<StorageBreakdown> ReadAsync(Guid tenantId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(breakdown);
        }
    }
}
