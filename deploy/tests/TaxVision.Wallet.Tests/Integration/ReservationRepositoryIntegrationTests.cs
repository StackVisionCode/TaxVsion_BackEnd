using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using TaxVision.Wallet.Tests.TestDoubles;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>Contra el SQL Server local: la búsqueda de candidatos del job traduce a SQL y respeta tipo y exclusión.</summary>
public sealed class ReservationRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _referenceType = $"tint-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        db.Reservations.Add(ReservationBuilder.Open(_tenantId, _referenceType, Guid.NewGuid(), 10_000, 1));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        await db.Reservations.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ListOpenOlderThanAsync_FiltersByTypeAndExclusion()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var repository = new ReservationRepository(db);
        var cutoff = DateTime.UtcNow.AddMinutes(1);

        var byType = await repository.ListOpenOlderThanAsync(cutoff, 10, _referenceType, null);
        var excluded = await repository.ListOpenOlderThanAsync(cutoff, 10, _referenceType, [_referenceType]);
        var notYetOld = await repository.ListOpenOlderThanAsync(cutoff.AddHours(-1), 10, _referenceType, null);

        var found = Assert.Single(byType);
        Assert.Equal(_tenantId, found.TenantId);
        Assert.Empty(excluded);
        Assert.Empty(notYetOld);
    }
}
