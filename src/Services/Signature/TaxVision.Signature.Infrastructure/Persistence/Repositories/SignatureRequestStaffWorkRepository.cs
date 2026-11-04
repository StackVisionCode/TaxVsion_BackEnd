#pragma warning disable CS0618 // Ready está obsoleto pero las filas históricas siguen contando como borrador.
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

// tenantId llega explícito desde el JWT o el evento: IgnoreQueryFilters() porque en consumers Wolverine
// no hay TenantContext ambiente (mismo criterio que SignatureRequestRepository).
public sealed class SignatureRequestStaffWorkRepository(SignatureDbContext db) : ISignatureRequestStaffWorkRepository
{
    public async Task<IReadOnlyList<SignatureRequest>> ListUnsentAuthoredByAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await WithGraph()
            .Where(r => r.TenantId == tenantId && r.CreatedByUserId == userId)
            .Where(Unsent())
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SignatureRequest>> ListOpenWithPreparerAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await WithGraph()
            .Where(r =>
                r.TenantId == tenantId
                && r.Preparer != null
                && r.Preparer.UserId == userId
                && r.PreparerSignedByUserId == null
                && (
                    r.Status == SignatureRequestStatus.Draft
                    || r.Status == SignatureRequestStatus.Ready
                    || r.Status == SignatureRequestStatus.Scheduled
                    || r.Status == SignatureRequestStatus.InProgress
                )
            )
            .ToListAsync(ct);

    public async Task<SignatureOffboardingCounts> CountOffboardingImpactAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var asPreparer = db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r =>
                r.TenantId == tenantId
                && r.Preparer != null
                && r.Preparer.UserId == userId
                && r.PreparerSignedByUserId == null
            );

        var pending = await asPreparer.CountAsync(
            r => r.Status == SignatureRequestStatus.Scheduled || r.Status == SignatureRequestStatus.InProgress,
            ct
        );
        var onDrafts = await asPreparer.CountAsync(
            r => r.Status == SignatureRequestStatus.Draft || r.Status == SignatureRequestStatus.Ready,
            ct
        );
        var drafts = await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.CreatedByUserId == userId)
            .Where(Unsent())
            .CountAsync(ct);

        return new SignatureOffboardingCounts(pending, onDrafts, drafts);
    }

    // Primero donde el usuario firmó como preparador (la identidad quedó ratificada con su firma); después
    // la última que declaró. Solo lectura: no se rastrea.
    public async Task<PreparerIdentitySnapshot?> FindLatestPreparerIdentityAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Preparer != null && r.Preparer.UserId == userId)
            .OrderByDescending(r => r.PreparerSignedByUserId == userId)
            .ThenByDescending(r => r.UpdatedAtUtc)
            .Select(r => new PreparerIdentitySnapshot(
                r.Preparer!.PtinOrEfin,
                r.Preparer.DisplayName,
                r.Preparer.TitleLabel
            ))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListUsersWithPreparerIdentityAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Preparer != null && r.Preparer.UserId != null)
            .Select(r => r.Preparer!.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

    private static System.Linq.Expressions.Expression<Func<SignatureRequest, bool>> Unsent() =>
        r =>
            r.Status == SignatureRequestStatus.Draft
            || r.Status == SignatureRequestStatus.Ready
            || r.Status == SignatureRequestStatus.Scheduled;

    // Mismo grafo que SignatureRequestRepository.GetByIdAsync: Remove() y las mutaciones lo necesitan cargado.
    private IQueryable<SignatureRequest> WithGraph() =>
        db
            .SignatureRequests.IgnoreQueryFilters()
            .Include(request => request.Signers)
                .ThenInclude(signer => signer.Fields)
            .Include(request => request.Signers)
                .ThenInclude(signer => signer.Challenges)
            .Include(request => request.Signers)
                .ThenInclude(signer => signer.FieldValues)
            .Include(request => request.Signers)
                .ThenInclude(signer => signer.DocumentViews)
            .Include(request => request.Signers)
                .ThenInclude(signer => signer.DocumentCompletions)
            .Include(request => request.PreparerFields)
            .Include(request => request.Documents)
            .Include(request => request.CustomerCopies)
            .AsSplitQuery();
}
