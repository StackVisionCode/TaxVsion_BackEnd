using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.OAuth;

/// <summary>Scope y DbContext propios: el guardado no entra en la transacción del que llamó.</summary>
public sealed class OAuthTokenStore(IServiceScopeFactory scopeFactory) : IOAuthTokenStore
{
    public async Task SaveAsync(Guid accountId, Action<OAuthToken> update, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var connection = await scope
            .ServiceProvider.GetRequiredService<IOAuthConnectionRepository>()
            .GetByAccountIdAsync(accountId, ct);
        if (connection.IsFailure || connection.Value.Token is null)
            return;

        update(connection.Value.Token);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
    }
}
