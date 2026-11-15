using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Administration;
using TaxVision.CloudStorage.Domain.Providers;
using Xunit;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// P23 (backup a Drive): la zona del backup es IANA. Un id de Windows depende del sistema que corra el job, así que
/// se rechaza; uno IANA se guarda normalizado.
/// </summary>
public sealed class SetStorageProviderHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task A_windows_time_zone_is_rejected()
    {
        var result = await Run("Eastern Standard Time", new Policies());

        Assert.Equal("StoragePolicy.InvalidTimeZone", result.Error.Code);
    }

    [Fact]
    public async Task An_iana_time_zone_is_stored_normalized()
    {
        var policies = new Policies();

        var result = await Run("  America/New_York ", policies);

        Assert.True(result.IsSuccess);
        Assert.Equal("America/New_York", policies.Saved?.BackupTimeZoneId);
    }

    private static Task<BuildingBlocks.Results.Result> Run(string zone, Policies policies) =>
        SetStorageProviderHandler.Handle(
            new SetStorageProviderCommand(Tenant, "backup", "google-drive", "daily", "02:30", zone),
            policies,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    private sealed class Policies : IStoragePolicyRepository
    {
        public TenantStoragePolicy? Saved { get; private set; }

        public void Add(TenantStoragePolicy policy) => Saved = policy;

        public Task<TenantStoragePolicy?> GetAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(Saved);

        public Task<IReadOnlyList<string>> ListConnectedProviderIdsAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
