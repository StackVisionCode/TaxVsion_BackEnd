using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Infrastructure.Providers;

/// <summary>
/// Indexa los <see cref="IOffSessionCharger"/> registrados por su <see cref="IOffSessionCharger.ProviderCode"/>.
/// Cada charger expone su propio código — así agregar PayPal/Braintree no toca este factory.
/// Devuelve null cuando el provider no está registrado (Manual, por ejemplo): el caller decide
/// si eso es error de negocio o de config.
/// </summary>
public sealed class OffSessionChargerFactory : IOffSessionChargerFactory
{
    private readonly IReadOnlyDictionary<string, IOffSessionCharger> _byCode;

    public OffSessionChargerFactory(IEnumerable<IOffSessionCharger> chargers)
    {
        _byCode = chargers.ToDictionary(c => c.ProviderCode, StringComparer.OrdinalIgnoreCase);
    }

    public IOffSessionCharger? For(string providerCode) =>
        !string.IsNullOrWhiteSpace(providerCode) && _byCode.TryGetValue(providerCode, out var charger) ? charger : null;
}
