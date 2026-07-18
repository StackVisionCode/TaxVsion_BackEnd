using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Completa el flujo de conectar cuenta (D3 §12) tras el callback de autorización — intercambia el
/// authorization code, resuelve la cuenta y arma la OAuthConnection. TenantId/ProviderCode/InitiatedByUserId
/// vienen del <c>OAuthConnectState</c> consumido por el caller (nunca del query string del callback,
/// que un atacante podría manipular).
/// </summary>
public sealed record CompleteOAuthConnectCommand(
    Guid TenantId,
    ProviderCode ProviderCode,
    Guid InitiatedByUserId,
    string AuthorizationCode
);

public sealed record CompleteOAuthConnectResult(Guid AccountId, string EmailAddress);
