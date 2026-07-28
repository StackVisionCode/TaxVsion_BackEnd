using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>Token opaco que identifica al "customer" en el provider (Stripe <c>cus_xxx</c>,
/// Intellipay <c>custId</c>). El Domain nunca ve esto — solo circula entre Application y los
/// adapters de Infrastructure.</summary>
public sealed record ProviderCustomerToken(string Token, PaymentProviderCode Provider);

/// <summary>Token opaco de un método de pago específico ya tokenizado en el provider
/// (p.ej. un <c>pm_xxx</c> de Stripe elegido explícitamente en vez del default del customer).</summary>
public sealed record PaymentMethodToken(string Token);

/// <summary>Resultado de crear un SetupIntent en el provider: el <c>client_secret</c> que el
/// frontend (Stripe Payment Element) usa para recolectar y confirmar la tarjeta sin que el PAN
/// toque el backend.</summary>
public sealed record SetupIntentInfo(string ClientSecret);
