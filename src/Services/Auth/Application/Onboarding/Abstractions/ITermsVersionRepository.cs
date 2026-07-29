using TaxVision.Auth.Domain.Onboarding.TermsVersions;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public interface ITermsVersionRepository
{
    Task AddAsync(TermsVersion version, CancellationToken ct = default);

    Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Version vigente para Kind+Locale al momento nowUtc — la mas reciente con EffectiveFromUtc &lt;= nowUtc y (EffectiveUntilUtc es null o &gt; nowUtc).</summary>
    Task<TermsVersion?> GetCurrentAsync(TermsKind kind, string locale, DateTime nowUtc, CancellationToken ct = default);
}
