using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.OAuth;

/// <summary>Scope y DbContext propios: el guardado no entra en la transacción del que llamó.</summary>
public sealed class OAuthTokenStore(IServiceScopeFactory scopeFactory, ILogger<OAuthTokenStore> logger)
    : IOAuthTokenStore
{
    public async Task<bool> SaveAsync(
        Guid accountId,
        byte[] expectedRowVersion,
        Action<OAuthToken> update,
        CancellationToken ct = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var connection = await scope
            .ServiceProvider.GetRequiredService<IOAuthConnectionRepository>()
            .GetByAccountIdAsync(accountId, ct);
        if (connection.IsFailure || connection.Value.Token is not { } token)
            return false;

        // Cambió desde que se leyó: otro guardó primero y el suyo queda.
        if (!token.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
            return KeptTheOtherWrite(accountId);

        update(token);
        try
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            return true;
        }
        catch (ConflictException)
        {
            return KeptTheOtherWrite(accountId);
        }
    }

    private bool KeptTheOtherWrite(Guid accountId)
    {
        logger.LogInformation(
            "OAuth token of account {AccountId} was saved concurrently; the token saved first is kept.",
            accountId
        );
        return false;
    }
}
