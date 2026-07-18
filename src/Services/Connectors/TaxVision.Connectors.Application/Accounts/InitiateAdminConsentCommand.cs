namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Fallback de admin-consent para Graph (D3 §12.6) — solo se necesita cuando el connect normal (D3
/// §12.4) falló con AADSTS90094/consent_required. La mayoría de los tenants nunca lo van a usar
/// porque Mail.Send delegado no pide admin-consent por default.
/// </summary>
public sealed record InitiateAdminConsentCommand(Guid TenantId, Guid InitiatedByUserId);

public sealed record InitiateAdminConsentResult(string Url);
