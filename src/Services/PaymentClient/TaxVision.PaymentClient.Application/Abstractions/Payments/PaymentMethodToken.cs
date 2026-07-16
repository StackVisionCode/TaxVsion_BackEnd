namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>Token opaco de un método de pago ya tokenizado en el provider por el cliente
/// (p.ej. un <c>pm_xxx</c> de Stripe Elements). PaymentClient cobra taxpayers en modo
/// guest-checkout — no existe un customer guardado como en PaymentApp — así que el método
/// viaja directo en cada request de cobro.</summary>
public sealed record PaymentMethodToken(string Token);
