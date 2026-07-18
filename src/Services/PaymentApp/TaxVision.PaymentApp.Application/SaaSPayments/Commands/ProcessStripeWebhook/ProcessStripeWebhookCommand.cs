namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessStripeWebhook;

/// <summary>Payload crudo tal como llegó al controller — la verificación de firma ocurre
/// dentro del handler, nunca antes (el controller no decide qué es válido).</summary>
public sealed record ProcessStripeWebhookCommand(string RawPayload, string SignatureHeader);
