using Microsoft.Extensions.Options;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>Opciones fijas para los tests: el Gateway lee siempre <c>CurrentValue</c>.</summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
