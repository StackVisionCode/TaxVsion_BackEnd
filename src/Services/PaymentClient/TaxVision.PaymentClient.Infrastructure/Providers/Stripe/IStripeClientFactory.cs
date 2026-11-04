using Stripe;

namespace TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

/// <summary>Crea el <see cref="StripeClient"/> de cada llamada con la clave de la oficina. Costura para
/// probar el adaptador sin red; la implementación real es la de siempre.</summary>
public interface IStripeClientFactory
{
    IStripeClient Create(string secretKey);
}

public sealed class StripeClientFactory : IStripeClientFactory
{
    public static readonly StripeClientFactory Default = new();

    public IStripeClient Create(string secretKey) => new StripeClient(secretKey);
}
