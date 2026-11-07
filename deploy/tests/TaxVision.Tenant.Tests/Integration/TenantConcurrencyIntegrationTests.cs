using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Domain.Enums;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>TEN-4 contra SQL real: la versión es el token de concurrencia de la fila. Base temporal propia.</summary>
public sealed class TenantConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly TenantTemporaryDatabase _database = new();
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        var tenant = DomainTenant.Create("Acme", "acme-tax", "Etc/UTC").Value;
        _tenantId = tenant.Id;
        await using var db = _database.NewContext();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DropAsync();

    [Fact]
    public async Task TwoStatusChanges_SecondSaveConflicts()
    {
        await using var first = _database.NewContext();
        await using var second = _database.NewContext();
        var suspending = await first.Tenants.SingleAsync(tenant => tenant.Id == _tenantId);
        var closing = await second.Tenants.SingleAsync(tenant => tenant.Id == _tenantId);

        Assert.True(suspending.Suspend().IsSuccess);
        Assert.True(closing.ChangeStatus(EnumTenantStatus.TenantStatus.Closed).IsSuccess);

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        await using var read = _database.NewContext();
        Assert.Equal(1, (await read.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == _tenantId)).Version);
    }
}
