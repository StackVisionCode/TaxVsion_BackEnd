namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>Método de pago canónico, independiente del provider. Cada
/// <see cref="ProviderCapabilities"/> declara qué subconjunto soporta.</summary>
public enum PaymentMethodKind
{
    Card = 1,
    ApplePay = 2,
    GooglePay = 3,
    AchDebit = 4,
    SepaDebit = 5,
    Wallet = 6,
    Crypto = 7,
    Bnpl = 8,
    Bank = 9,
    Cash = 10,
    Manual = 11,
}
