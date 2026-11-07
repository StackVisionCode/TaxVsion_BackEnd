using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.OAuth;

/// <summary>
/// Guarda un token refrescado en su propia confirmación, ajena a la del que lo pidió: Microsoft rota el refresh
/// token, y si se revirtiera con el handler, el siguiente refresh usaría uno ya invalidado.
/// </summary>
public interface IOAuthTokenStore
{
    Task SaveAsync(Guid accountId, Action<OAuthToken> update, CancellationToken ct = default);
}
