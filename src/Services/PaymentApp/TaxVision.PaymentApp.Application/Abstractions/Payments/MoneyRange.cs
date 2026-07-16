namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>Rango de monto que un provider acepta, en la unidad menor de la moneda
/// (p.ej. centavos). Parte declarativa de <see cref="ProviderCapabilities"/> — no es una
/// entidad de dominio, solo metadata del adapter.</summary>
public sealed record MoneyRange(long MinAmountCents, long MaxAmountCents, string Currency);
