using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Tenants.Queries;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A4 — GET by-host expone esto para el TenantId ya resuelto por el middleware.</summary>
public sealed class GetTenantPublicInfoTests
{
    private sealed class FakeTenantRegistry : ITenantRegistry
    {
        public Tenant? TenantToReturn { get; set; }

        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(TenantToReturn);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Active_tenant_returns_its_public_info()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value;
        var tenants = new FakeTenantRegistry { TenantToReturn = tenant };

        var result = await GetTenantPublicInfoHandler.Handle(
            new GetTenantPublicInfoQuery(tenantId),
            tenants,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal("Oficina 1", result.Value.Name);
        Assert.Equal("Active", result.Value.Status);
        Assert.Null(result.Value.LogoUrl);
    }

    [Fact]
    public async Task Missing_tenant_fails_with_not_found()
    {
        var result = await GetTenantPublicInfoHandler.Handle(
            new GetTenantPublicInfoQuery(Guid.NewGuid()),
            new FakeTenantRegistry(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Inactive_tenant_fails_with_not_found()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value;
        tenant.Deactivate();
        var tenants = new FakeTenantRegistry { TenantToReturn = tenant };

        var result = await GetTenantPublicInfoHandler.Handle(
            new GetTenantPublicInfoQuery(tenantId),
            tenants,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.NotFound", result.Error.Code);
    }
}
