using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Persistence;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>CON-04 contra SQL real, en base temporal: quien leyó viejo no pisa a quien guardó primero.</summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class TenantEmailAccountConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("conacct");

    public Task InitializeAsync() => _host.StartAsync(_ => { });

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task MarkErrorAfterDisconnect_FromStaleRead_ThrowsConcurrency()
    {
        var accountId = await SeedActiveAccountAsync();

        await using var first = _host.NewContext();
        await using var second = _host.NewContext();
        var winner = await LoadAsync(first, accountId);
        var stale = await LoadAsync(second, accountId);

        winner.Disconnect(DateTime.UtcNow);
        await first.SaveChangesAsync();

        stale.MarkError(
            new TenantEmailAccountErrorCause(TenantEmailAccountErrorSource.OAuthRefresh, "x", "y", "corr"),
            DateTime.UtcNow
        );
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = _host.NewContext();
        Assert.Equal(TenantEmailAccountStatus.Disconnected, (await LoadAsync(read, accountId)).Status);
    }

    private static Task<TenantEmailAccount> LoadAsync(ConnectorsDbContext db, Guid accountId) =>
        db.TenantEmailAccounts.IgnoreQueryFilters().SingleAsync(a => a.Id == accountId);

    private async Task<Guid> SeedActiveAccountAsync()
    {
        var now = DateTime.UtcNow;
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.test", ProviderCode.Gmail, Guid.NewGuid(), now)
            .Value;
        account.MarkConnected(now);
        account.Activate(now);
        await using var db = _host.NewContext();
        db.TenantEmailAccounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }
}
