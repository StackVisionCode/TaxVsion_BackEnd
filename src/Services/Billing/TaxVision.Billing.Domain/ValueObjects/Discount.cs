namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Descuento a nivel de factura. Value = basis points (Percentage) o cents (Fixed).
/// Amount = monto aplicado congelado.</summary>
public sealed record Discount(DiscountType Type, int Value, Money Amount);
