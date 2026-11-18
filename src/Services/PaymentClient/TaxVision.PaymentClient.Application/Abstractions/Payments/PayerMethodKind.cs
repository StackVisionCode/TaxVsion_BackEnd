namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Cómo ve quien paga a un proveedor. Con esto el checkout arma sus textos sin conocer proveedores:
/// tarjeta → "Pay $X" (el procesador no se nombra); billetera → "Pay with {marca}"; banco → "Pay $X by bank".
/// </summary>
public enum PayerMethodKind
{
    Card = 1,
    Wallet = 2,
    Bank = 3,
}
