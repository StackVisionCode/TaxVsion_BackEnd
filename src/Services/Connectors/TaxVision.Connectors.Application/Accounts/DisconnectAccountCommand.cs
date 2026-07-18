namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// <c>DELETE /connectors/accounts/{id}</c> (D3 §12.4). Para Graph, la desconexión es solo del lado de
/// TaxVision — el consentimiento en Microsoft sigue vivo hasta que el usuario lo revoque él mismo
/// desde myaccount.microsoft.com/consents (D3 §12.8, Graph no expone una API de revocación).
/// </summary>
public sealed record DisconnectAccountCommand(Guid TenantId, Guid AccountId);
