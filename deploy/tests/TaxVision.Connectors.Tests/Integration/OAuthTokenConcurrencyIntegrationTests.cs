using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>Connectors H: el token OAuth lleva su propio RowVersion; una lectura vieja no pisa un refresco guardado.</summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class OAuthTokenConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("contokconc");
    private readonly FakeEncryptedSecretProtector _protector = new();

    public Task InitializeAsync() => _host.StartAsync(_ => { });

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task TwoRefreshes_FromStaleReads_SecondConflicts()
    {
        var now = DateTime.UtcNow;
        var connection = OAuthConnection.Create(Guid.NewGuid(), ProviderCode.Graph, "client", "scope", now).Value;
        connection.AttachToken(
            OAuthToken
                .Create(connection.Id, _protector.Protect("old-access"), _protector.Protect("old-refresh"), now, now)
                .Value
        );
        await using (var seed = _host.NewContext())
        {
            seed.OAuthConnections.Add(connection);
            await seed.SaveChangesAsync();
        }

        await using var first = _host.NewContext();
        await using var second = _host.NewContext();
        var winner = await first.OAuthTokens.SingleAsync(t => t.ConnectionId == connection.Id);
        var stale = await second.OAuthTokens.SingleAsync(t => t.ConnectionId == connection.Id);

        winner.UpdateAccessToken(_protector.Protect("first-access"), now.AddHours(1), now);
        winner.UpdateRefreshToken(_protector.Protect("first-refresh"));
        await first.SaveChangesAsync();

        stale.UpdateAccessToken(_protector.Protect("second-access"), now.AddHours(1), now);
        stale.UpdateRefreshToken(_protector.Protect("second-refresh"));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = _host.NewContext();
        var stored = await read.OAuthTokens.AsNoTracking().SingleAsync(t => t.ConnectionId == connection.Id);
        Assert.Equal("first-refresh", _protector.Unprotect(stored.RefreshTokenCipher));
    }
}
