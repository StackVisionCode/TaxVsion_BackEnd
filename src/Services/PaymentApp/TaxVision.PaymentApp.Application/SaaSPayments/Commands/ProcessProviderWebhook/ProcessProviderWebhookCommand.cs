using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;

/// <summary>
/// Generic public webhook ingress command. The controller passes the raw provider payload and
/// headers unchanged; Application verifies through the selected provider adapter before trusting it.
/// </summary>
public sealed record ProcessProviderWebhookCommand(
    PaymentProviderCode Provider,
    string RawPayload,
    IReadOnlyDictionary<string, string> Headers
);
