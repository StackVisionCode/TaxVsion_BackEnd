using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

public interface IOAuthConnectionRepository
{
    Task AddAsync(OAuthConnection connection, CancellationToken ct = default);

    /// <summary>Incluye el OAuthToken hijo — el caller siempre lo necesita junto con la connection.</summary>
    Task<Result<OAuthConnection>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Sin tracking: el token se guarda en su propia unidad y una lectura rastreada lo vería viejo.</summary>
    Task<Result<OAuthConnection>> GetByAccountIdReadOnlyAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// AccountIds cuyo AccessToken expira antes de <paramref name="thresholdUtc"/> y cuya connection
    /// sigue Active — usado por ProactiveTokenRefreshJob (Fase 4) para no cargar aggregates completos
    /// de cuentas que no lo necesitan. Excluye las cuentas en Error, Disconnected o Draft: su refresh
    /// token ya falló o no aplica, y reintentarlo solo vuelve a fallar.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAccountIdsWithTokenExpiringBeforeAsync(
        DateTime thresholdUtc,
        CancellationToken ct = default
    );

    /// <summary>Scope aceptado de cada conexión ACTIVA de esas cuentas (proyección, sin cargar tokens).</summary>
    Task<IReadOnlyDictionary<Guid, string>> ListActiveScopesByAccountIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default
    );

    /// <summary>Purga la connection (y su token hijo) al retirar al dueño del buzón.</summary>
    void Remove(OAuthConnection connection);
}
