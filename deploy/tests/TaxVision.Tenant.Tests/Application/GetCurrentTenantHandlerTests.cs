using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Queries;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

public sealed class GetCurrentTenantHandlerTests
{
    [Fact]
    public async Task Returns_id_name_subdomain_and_time_zone_of_the_tenant()
    {
        var tenant = TenantEntity.Create("Acme Tax", "acme-tax", "America/Chicago", DateTime.UtcNow).Value;

        var result = await GetCurrentTenantHandler.Handle(
            new GetCurrentTenantQuery(tenant.Id),
            new FakeTenantRepository(tenant),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(tenant.Id, result.Value.Id);
        Assert.Equal("Acme Tax", result.Value.Name);
        Assert.Equal("acme-tax", result.Value.Subdomain);
        Assert.Equal("America/Chicago", result.Value.DefaultTimeZoneId);
    }

    [Fact]
    public async Task Unknown_tenant_is_not_found()
    {
        var result = await GetCurrentTenantHandler.Handle(
            new GetCurrentTenantQuery(Guid.NewGuid()),
            new FakeTenantRepository(null),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.NotFound", result.Error.Code);
    }

    private sealed class FakeTenantRepository(TenantEntity? existing) : ITenantRepository
    {
        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(existing is not null && existing.Id == id ? existing : null);

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => throw new NotSupportedException();

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
