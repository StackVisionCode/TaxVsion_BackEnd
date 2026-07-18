namespace TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;

public sealed record ProcessConnectWebhookCommand(string RawPayload, string SignatureHeader);
