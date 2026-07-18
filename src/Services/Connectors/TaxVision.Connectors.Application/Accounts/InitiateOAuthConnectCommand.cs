using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Arranca el flujo de conectar cuenta (D3 §12.4) — <c>POST /connectors/accounts</c>. El frontend
/// redirige el navegador del usuario a <c>AuthorizationUrl</c>; no es un fetch normal, es una
/// navegación de página completa (el consentimiento vive en el dominio de Google/Microsoft).
/// </summary>
public sealed record InitiateOAuthConnectCommand(Guid TenantId, ProviderCode ProviderCode, Guid InitiatedByUserId);

public sealed record InitiateOAuthConnectResult(string AuthorizationUrl);
