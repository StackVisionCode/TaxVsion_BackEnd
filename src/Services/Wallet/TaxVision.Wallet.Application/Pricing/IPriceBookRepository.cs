using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Application.Pricing;

/// <summary>Catálogo de precios versionado (global de plataforma). La versión vigente = mayor Version
/// publicada con EffectiveFromUtc ≤ ahora.</summary>
public interface IPriceBookRepository
{
    Task<PriceBookVersion?> GetActiveVersionAsync(CancellationToken ct = default);

    /// <summary>Mayor número de versión existente (0 si no hay ninguna) — para numerar la siguiente.</summary>
    Task<int> GetMaxVersionAsync(CancellationToken ct = default);

    Task AddVersionAsync(PriceBookVersion version, CancellationToken ct = default);
}
