namespace TaxVision.Scribe.Infrastructure.Rendering;

/// <summary>Degradación cuando Redis no está configurado (dev local): siempre falla el hit, el renderer cae a CloudStorage.</summary>
public sealed class NoOpTemplateSourceCache : ITemplateSourceCache
{
    public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task SetAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
}
