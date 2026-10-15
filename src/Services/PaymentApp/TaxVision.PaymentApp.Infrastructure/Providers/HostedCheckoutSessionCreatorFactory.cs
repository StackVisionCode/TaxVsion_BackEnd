using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Infrastructure.Providers;

/// <summary>Indexa los creators por ProviderCode. Fix de un bug Wolverine-DI:
/// con AddScoped(default) + AddKeyedScoped el codegen resuelve el keyed como si fuera
/// el default, sirviendo PayPal donde se pidió Stripe. El factory selecciona explícito.</summary>
public sealed class HostedCheckoutSessionCreatorFactory : IHostedCheckoutSessionCreatorFactory
{
    private readonly IReadOnlyDictionary<string, IHostedCheckoutSessionCreator> _byCode;

    public HostedCheckoutSessionCreatorFactory(IEnumerable<IHostedCheckoutSessionCreator> creators)
    {
        _byCode = creators.ToDictionary(c => c.ProviderCode, StringComparer.OrdinalIgnoreCase);
    }

    public IHostedCheckoutSessionCreator? For(string? providerCode) =>
        !string.IsNullOrWhiteSpace(providerCode) && _byCode.TryGetValue(providerCode, out var creator) ? creator : null;
}
