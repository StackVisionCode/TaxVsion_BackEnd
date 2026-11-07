using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Infrastructure.Audit;

/// <summary>SIG-17: un scope y una transacción por solicitud, con el candado de SIG-4.</summary>
public sealed class CompletedAuditBackfillStore(IServiceScopeFactory scopes) : ICompletedAuditBackfillStore
{
    public async Task<IReadOnlyList<CompletedAuditGap>> ListAsync(Guid? tenantId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();

        // Cross-tenant a propósito (comando de plataforma): el filtro va explícito.
        var requests = await db
            .SignatureRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(request =>
                request.Status == SignatureRequestStatus.Completed
                && (tenantId == null || request.TenantId == tenantId)
                && !db
                    .SignatureAuditEvents.IgnoreQueryFilters()
                    .Any(line =>
                        line.SignatureRequestId == request.Id && line.Kind == SignatureAuditEventKind.RequestCompleted
                    )
            )
            .OrderBy(request => request.CompletedAtUtc)
            .Select(request => new
            {
                request.TenantId,
                request.Id,
                CompletedAtUtc = request.CompletedAtUtc ?? request.UpdatedAtUtc,
                request.GenerateCertificate,
                ExistingLines = db
                    .SignatureAuditEvents.IgnoreQueryFilters()
                    .Count(line => line.SignatureRequestId == request.Id),
            })
            .ToListAsync(ct);
        if (requests.Count == 0)
            return [];

        var ids = requests.Select(request => request.Id).ToList();
        var documents = await db
            .RequestDocuments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(document => ids.Contains(document.SignatureRequestId) && document.DocumentHashPre != null)
            .Select(document => new
            {
                document.SignatureRequestId,
                Descriptor = new DocumentHashDescriptor(
                    document.Id,
                    document.OriginalFileId,
                    document.DocumentHashPre!.Value
                ),
            })
            .ToListAsync(ct);
        var byRequest = documents.ToLookup(document => document.SignatureRequestId, document => document.Descriptor);

        return requests
            .Select(request => new CompletedAuditGap(
                request.TenantId,
                request.Id,
                request.CompletedAtUtc,
                request.GenerateCertificate,
                byRequest[request.Id].ToList(),
                request.ExistingLines
            ))
            .ToList();
    }

    public async Task<Result<bool>> AppendIfMissingAsync(
        CompletedAuditGap gap,
        object payload,
        CancellationToken ct = default
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Espera a un append en curso de la misma solicitud y relee con el candado tomado.
        await scope.ServiceProvider.GetRequiredService<SqlAuditChainLock>().AcquireAsync(gap.SignatureRequestId, ct);
        var present = await db
            .SignatureAuditEvents.IgnoreQueryFilters()
            .AnyAsync(
                line =>
                    line.SignatureRequestId == gap.SignatureRequestId
                    && line.Kind == SignatureAuditEventKind.RequestCompleted,
                ct
            );
        if (present)
            return Result.Success(false);

        var appended = await scope
            .ServiceProvider.GetRequiredService<IAuditChainAppender>()
            .AppendAsync(
                gap.TenantId,
                gap.SignatureRequestId,
                SignatureAuditEventKind.RequestCompleted,
                gap.CompletedAtUtc,
                payload,
                ct
            );
        if (appended.IsFailure)
            return Result.Failure<bool>(appended.Error);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(true);
    }
}
