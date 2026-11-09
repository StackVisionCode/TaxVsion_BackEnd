using BuildingBlocks.Caching;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Tests.TestSupport;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>PUT tenants/current: guarda y el agregado anuncia el estado completo; sin cambios no
/// publica; un dato inválido no guarda ni publica nada.</summary>
public sealed class UpdateCurrentTenantHandlerTests
{
    private const string TenantListVersionKey = "tenants:list:v2:version";

    [Fact]
    public async Task Rename_and_time_zone_change_saves_publishes_full_state_and_invalidates_the_list()
    {
        var tenant = TenantEntity.Create("Acme Tax", "acme-tax", "Etc/UTC", DateTime.UtcNow).Value;
        var (repo, unitOfWork, cache) = Arrange(tenant);

        var result = await HandleAsync(
            new(tenant.Id, "Acme Tax Group", "America/Chicago"),
            repo,
            unitOfWork,
            cache
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme Tax Group", result.Value.Name);
        Assert.Equal("America/Chicago", result.Value.DefaultTimeZoneId);
        Assert.Equal("acme-tax", result.Value.Subdomain);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var evt = Assert.Single(TenantDomainEventReplay.Dispatch(tenant.DomainEvents).OfType<TenantUpdatedIntegrationEvent>());
        Assert.Equal(tenant.Id, evt.UpdatedTenantId);
        Assert.Equal(tenant.Id, evt.TenantId);
        Assert.Equal("Acme Tax Group", evt.Name);
        Assert.Equal("America/Chicago", evt.DefaultTimeZoneId);
        Assert.Equal(tenant.UpdatedAtUtc, evt.UpdatedAtUtc);
        Assert.Equal([TenantListVersionKey], cache.SetKeys);
    }

    [Fact]
    public async Task Same_values_return_the_current_state_without_saving_or_publishing()
    {
        var tenant = TenantEntity.Create("Acme Tax", "acme-tax", "America/Chicago", DateTime.UtcNow).Value;
        var (repo, unitOfWork, cache) = Arrange(tenant);

        var result = await HandleAsync(new(tenant.Id, " Acme Tax ", "America/Chicago"), repo, unitOfWork, cache);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(tenant.DomainEvents);
        Assert.Empty(cache.SetKeys);
    }

    [Theory]
    [InlineData("", "America/Chicago", "Tenant.Name")]
    [InlineData("Acme Tax Group", "Not/AZone", "Tenant.DefaultTimeZoneId")]
    public async Task Invalid_input_returns_a_coded_error_and_changes_nothing(
        string name,
        string timeZoneId,
        string expectedCode
    )
    {
        var tenant = TenantEntity.Create("Acme Tax", "acme-tax", "Etc/UTC", DateTime.UtcNow).Value;
        var (repo, unitOfWork, cache) = Arrange(tenant);

        var result = await HandleAsync(new(tenant.Id, name, timeZoneId), repo, unitOfWork, cache);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal("Acme Tax", tenant.Name);
        Assert.Equal("Etc/UTC", tenant.DefaultTimeZoneId);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(tenant.DomainEvents);
    }

    [Fact]
    public async Task Unknown_tenant_is_not_found()
    {
        var (repo, unitOfWork, cache) = Arrange(null);

        var result = await HandleAsync(new(Guid.NewGuid(), "Acme", "Etc/UTC"), repo, unitOfWork, cache);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.NotFound", result.Error.Code);
    }

    private static (FakeTenantRepository, CountingUnitOfWork, RecordingCache) Arrange(
        TenantEntity? tenant
    ) => (new FakeTenantRepository { Existing = tenant }, new(), new());

    private static Task<BuildingBlocks.Results.Result<TenantResponse>> HandleAsync(
        UpdateCurrentTenantCommand command,
        ITenantRepository repo,
        CountingUnitOfWork unitOfWork,
        ICacheService cache
    ) =>
        UpdateCurrentTenantHandler.Handle(
            command,
            repo,
            unitOfWork,
            cache,
            NullLogger<UpdateCurrentTenantCommand>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );

    private sealed class FakeTenantRepository : ITenantRepository
    {
        public TenantEntity? Existing { get; set; }

        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Existing is not null && Existing.Id == id ? Existing : null);

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => throw new NotSupportedException();

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingCache : ICacheService
    {
        public List<string> SetKeys { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            SetKeys.Add(key);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
