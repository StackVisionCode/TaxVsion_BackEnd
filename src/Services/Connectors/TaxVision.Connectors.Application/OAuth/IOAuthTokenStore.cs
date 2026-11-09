using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.OAuth;

/// <summary>
/// Guarda un token refrescado en su propia confirmación, ajena a la del que lo pidió: Microsoft rota el refresh
/// token, y si se revirtiera con el handler, el siguiente refresh usaría uno ya invalidado.
/// </summary>
public interface IOAuthTokenStore
{
    /// <summary>
    /// Guarda solo si el token sigue en la versión leída antes de llamar al proveedor. False: otro refresco (u otra
    /// reconexión) guardó primero y se conserva ese.
    /// </summary>
    Task<bool> SaveAsync(
        Guid accountId,
        byte[] expectedRowVersion,
        Action<OAuthToken> update,
        CancellationToken ct = default
    );
}
