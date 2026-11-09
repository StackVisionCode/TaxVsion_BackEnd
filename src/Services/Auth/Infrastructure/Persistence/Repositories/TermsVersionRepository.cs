using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Terms;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Onboarding.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class TermsVersionRepository(AuthDbContext db) : ITermsVersionRepository
{
    public async Task AddAsync(TermsVersion version, CancellationToken ct = default) =>
        await db.TermsVersions.AddAsync(version, ct);

    public Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.TermsVersions.FirstOrDefaultAsync(version => version.Id == id, ct);

    public Task<TermsVersion?> GetByContentFileIdAsync(Guid contentFileId, CancellationToken ct = default) =>
        db.TermsVersions.FirstOrDefaultAsync(version => version.ContentFileId == contentFileId, ct);

    public Task<TermsVersion?> GetCurrentAsync(
        TermsKind kind,
        string locale,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        db
            .TermsVersions.Where(version =>
                version.Kind == kind
                && version.Locale == locale
                && version.EffectiveFromUtc <= nowUtc
                && (version.EffectiveUntilUtc == null || version.EffectiveUntilUtc > nowUtc)
            )
            .OrderByDescending(version => version.EffectiveFromUtc)
            .FirstOrDefaultAsync(ct);
}
