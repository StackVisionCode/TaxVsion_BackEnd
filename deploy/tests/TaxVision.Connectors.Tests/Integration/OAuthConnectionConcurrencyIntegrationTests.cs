using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>CON-04: una lectura vieja no deja vencida una conexión recién reconectada.</summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class OAuthConnectionConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("conoconc");

    public Task InitializeAsync() => _host.StartAsync(_ => { });

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task ExpireAfterReconnect_FromStaleRead_Conflicts()
    {
        var now = DateTime.UtcNow;
        var connection = OAuthConnection.Create(Guid.NewGuid(), ProviderCode.Gmail, "client", "scope-a", now).Value;
        connection.Revoke(now);
        await using (var seed = _host.NewContext())
        {
            seed.OAuthConnections.Add(connection);
            await seed.SaveChangesAsync();
        }

        await using var first = _host.NewContext();
        await using var second = _host.NewContext();
        var winner = await first.OAuthConnections.SingleAsync(c => c.Id == connection.Id);
        var stale = await second.OAuthConnections.SingleAsync(c => c.Id == connection.Id);

        winner.Reconnect("client", "scope-a scope-b", now);
        await first.SaveChangesAsync();

        stale.Reconnect("client", "scope-a", now);
        stale.MarkExpired();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = _host.NewContext();
        Assert.Equal(
            OAuthConnectionStatus.Active,
            (await read.OAuthConnections.SingleAsync(c => c.Id == connection.Id)).Status
        );
    }
}
