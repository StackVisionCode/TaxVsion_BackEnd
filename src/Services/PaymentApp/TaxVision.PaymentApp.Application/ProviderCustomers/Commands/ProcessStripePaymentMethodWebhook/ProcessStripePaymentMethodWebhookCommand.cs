namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.ProcessStripePaymentMethodWebhook;

/// <summary>
/// Comando Wolverine que lleva el webhook crudo + headers de Stripe al handler encargado de
/// persistir el método tokenizado (<see cref="ProcessStripePaymentMethodWebhookHandler"/>). El
/// controller lo despacha para los eventos <c>checkout.session.completed</c> con guardado
/// off-session y <c>payment_method.attached</c>.
/// </summary>
public sealed record ProcessStripePaymentMethodWebhookCommand(string RawPayload, string SignatureHeader);
