namespace TaxVision.Scribe.Infrastructure.Rendering;

/// <summary>
/// L2 del AST cache: guarda el texto fuente crudo (no el AST parseado — Fluid.Ast no está pensado
/// para serialización binaria) con TTL indefinido. El renderer re-parsea en un hit de L2, lo cual es
/// barato; lo caro que evita este cache es el round-trip de red a CloudStorage.
/// </summary>
public interface ITemplateSourceCache
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
}
