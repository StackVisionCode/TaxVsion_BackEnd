using TaxVision.Scribe.Infrastructure.Rendering;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeTemplateSourceCache : ITemplateSourceCache
{
    private readonly Dictionary<string, string> _store = new();

    public int SetCalls { get; private set; }

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        SetCalls++;
        _store[key] = value;
        return Task.CompletedTask;
    }
}
