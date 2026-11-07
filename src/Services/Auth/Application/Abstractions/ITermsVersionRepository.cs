using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Application.Abstractions;

public interface ITermsVersionRepository
{
    Task AddAsync(TermsVersion version, CancellationToken ct = default);

    Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Version cuyo documento es ese FileId de CloudStorage, o null.</summary>
    Task<TermsVersion?> GetByContentFileIdAsync(Guid contentFileId, CancellationToken ct = default);

    /// <summary>Version vigente para Kind+Locale al momento nowUtc — la mas reciente con EffectiveFromUtc &lt;= nowUtc y (EffectiveUntilUtc es null o &gt; nowUtc).</summary>
    Task<TermsVersion?> GetCurrentAsync(TermsKind kind, string locale, DateTime nowUtc, CancellationToken ct = default);
}
