using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

public interface IOAuthConnectionRepository
{
    Task AddAsync(OAuthConnection connection, CancellationToken ct = default);

    /// <summary>Incluye el OAuthToken hijo — el caller siempre lo necesita junto con la connection.</summary>
    Task<Result<OAuthConnection>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// AccountIds cuyo AccessToken expira antes de <paramref name="thresholdUtc"/> y cuya connection
    /// sigue Active — usado por ProactiveTokenRefreshJob (Fase 4) para no cargar aggregates completos
    /// de cuentas que no lo necesitan.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAccountIdsWithTokenExpiringBeforeAsync(
        DateTime thresholdUtc,
        CancellationToken ct = default
    );
}
