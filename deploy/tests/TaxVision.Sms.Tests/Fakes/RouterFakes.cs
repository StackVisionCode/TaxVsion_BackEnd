using System.Diagnostics.CodeAnalysis;
using TaxVision.Sms.Application.Providers;

namespace TaxVision.Sms.Tests.Fakes;

/// <summary>Router de prueba: devuelve la lista de proveedores que le pasen (orden = failover).</summary>
internal sealed class FakeSmsProviderRouter(IReadOnlyList<ISmsProvider> providers) : ISmsProviderRouter
{
    public IReadOnlyList<ISmsProvider> ResolveOrder() => providers;
}

/// <summary>Factory de prueba con varios códigos mapeados (para probar el SmsProviderRouter real).</summary>
internal sealed class MapSmsAdapterFactory(IReadOnlyDictionary<string, ISmsProvider> byCode) : ISmsAdapterFactory
{
    public ISmsProvider Resolve(string code) =>
        byCode.TryGetValue(code, out var provider)
            ? provider
            : throw new InvalidOperationException($"No ISmsProvider is registered for provider code '{code}'.");

    public bool TryResolve(string code, [NotNullWhen(true)] out ISmsProvider? provider) =>
        byCode.TryGetValue(code, out provider);
}
