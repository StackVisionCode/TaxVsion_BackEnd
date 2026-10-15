namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.ProcessPayPalVaultWebhook;

/// <summary>
/// Dedicated ingress para eventos de PayPal Vault (<c>VAULT.PAYMENT-TOKEN.CREATED</c> / <c>DELETED</c>).
/// Van por un command distinto a <c>ProcessProviderWebhookCommand</c> porque no corresponden a un
/// <c>SaaSPayment</c>: el side-effect es sobre <c>TenantProviderCustomer.SavedMethods</c>.
/// </summary>
public sealed record ProcessPayPalVaultWebhookCommand(string RawPayload, IReadOnlyDictionary<string, string> Headers);
