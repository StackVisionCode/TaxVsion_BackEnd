using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Persistence;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// CON-03 paso 3: Microsoft rota el refresh token. Si el token nuevo se guardara con el que llamó, un rollback lo
/// perdería y el siguiente refresh usaría uno ya invalidado.
/// </summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class OAuthTokenRefreshIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("conrefresh");
    private readonly FakeOAuthProviderClient _provider = new(ProviderCode.Graph)
    {
        OnRefresh = _ => new OAuthTokenGrant("rotated-access", "rotated-refresh", 3600),
    };

    public Task InitializeAsync() =>
        _host.StartAsync(services =>
            services.AddScoped<IOAuthProviderClientFactory>(_ => new FakeOAuthProviderClientFactory(_provider))
        );

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task RefreshedToken_PersistsEvenIfCallerRollsBack()
    {
        var accountId = await SeedAsync();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ConnectorsDbContext>();
            await using var callerTransaction = await db.Database.BeginTransactionAsync();

            var token = await scope
                .ServiceProvider.GetRequiredService<IOAuthTokenManager>()
                .GetValidAccessTokenAsync(accountId);

            Assert.True(token.IsSuccess);
            await callerTransaction.RollbackAsync();
        }

        var protector = new FakeEncryptedSecretProtector();
        await using var read = _host.NewContext();
        var stored = await read.OAuthConnections.AsNoTracking().Include(c => c.Token).SingleAsync();
        Assert.Equal("rotated-refresh", protector.Unprotect(stored.Token!.RefreshTokenCipher));
        Assert.Equal("rotated-access", protector.Unprotect(stored.Token.AccessTokenCipher));
    }

    // Dos pedidos en el mismo scope: el segundo ve el token guardado y no vuelve a llamar al proveedor.
    [Fact(Timeout = 120_000)]
    public async Task SecondRequestInTheSameScope_SeesTheRefreshedToken()
    {
        var accountId = await SeedAsync();

        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOAuthTokenManager>();
        await manager.GetValidAccessTokenAsync(accountId);
        var second = await manager.GetValidAccessTokenAsync(accountId);

        Assert.Equal("rotated-access", second.Value);
        Assert.Equal(1, _provider.CallCount);
    }

    // Connectors H: dos refrescos leen el mismo token; el que guarda segundo no pisa al primero.
    [Fact(Timeout = 120_000)]
    public async Task ConcurrentRefresh_FromTheSameRead_KeepsTheTokenSavedFirst()
    {
        var accountId = await SeedAsync();
        var protector = new FakeEncryptedSecretProtector();
        _provider.OnRefresh = _ =>
        {
            // Otro nodo refresca y guarda mientras este espera al proveedor.
            using var other = _host.NewContext();
            var token = other.OAuthConnections.Include(c => c.Token).Single(c => c.AccountId == accountId).Token!;
            token.UpdateAccessToken(protector.Protect("other-access"), DateTime.UtcNow.AddHours(1), DateTime.UtcNow);
            token.UpdateRefreshToken(protector.Protect("other-refresh"));
            other.SaveChanges();
            return new OAuthTokenGrant("late-access", "late-refresh", 3600);
        };

        await using var scope = _host.Services.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IOAuthTokenManager>()
            .GetValidAccessTokenAsync(accountId);

        Assert.Equal("late-access", result.Value);
        await using var read = _host.NewContext();
        var stored = await read
            .OAuthConnections.AsNoTracking()
            .Include(c => c.Token)
            .SingleAsync(c => c.AccountId == accountId);
        Assert.Equal("other-refresh", protector.Unprotect(stored.Token!.RefreshTokenCipher));
        Assert.Equal("other-access", protector.Unprotect(stored.Token.AccessTokenCipher));
    }

    private async Task<Guid> SeedAsync()
    {
        var now = DateTime.UtcNow;
        var protector = new FakeEncryptedSecretProtector();
        var accountId = Guid.NewGuid();
        var connection = OAuthConnection.Create(accountId, ProviderCode.Graph, "client", "scope", now).Value;
        connection.AttachToken(
            OAuthToken
                .Create(
                    connection.Id,
                    protector.Protect("old-access"),
                    protector.Protect("old-refresh"),
                    now.AddMinutes(-5),
                    now
                )
                .Value
        );
        await using var db = _host.NewContext();
        db.OAuthConnections.Add(connection);
        await db.SaveChangesAsync();
        return accountId;
    }
}
