using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;

namespace TaxVision.Customer.Infrastructure.Persistence;

internal sealed class CustomerImportReadService(CustomerDbContext db) : ICustomerImportReadService
{
    public async Task<CustomerImportAttemptResponse?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var a = await db.CustomerImportAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : Map(a);
    }

    public async Task<IReadOnlyList<CustomerImportAttemptResponse>> SearchAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct
    )
    {
        return await db
            .CustomerImportAttempts.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(a => new CustomerImportAttemptResponse(
                a.Id,
                a.TenantId,
                a.CreatedByUserId,
                a.IdempotencyKey,
                a.Status,
                a.Strategy,
                a.SourceKind,
                a.SourceFileName,
                a.TotalRows,
                a.ProcessedRows,
                a.SuccessCount,
                a.UpdatedCount,
                a.SkippedCount,
                a.FailedCount,
                a.CreatedAtUtc,
                a.StartedAtUtc,
                a.CompletedAtUtc,
                a.CanceledAtUtc,
                a.CanceledByUserId,
                a.FailureReason
            ))
            .ToListAsync(ct);
    }

    public async IAsyncEnumerable<CustomerImportRowResponse> StreamRowsAsync(
        Guid importId,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var rows = db
            .CustomerImportRows.AsNoTracking()
            .Where(r => r.CustomerImportAttemptId == importId)
            .OrderBy(r => r.RowNumber)
            .AsAsyncEnumerable();

        await foreach (var r in rows.WithCancellation(ct))
        {
            yield return new CustomerImportRowResponse(
                r.RowNumber,
                r.Status,
                r.ResultingCustomerId,
                r.DisplayName,
                r.MatchedBy,
                r.ErrorCode,
                r.Message
            );
        }
    }

    private static CustomerImportAttemptResponse Map(Domain.Imports.CustomerImportAttempt a) =>
        new(
            a.Id,
            a.TenantId,
            a.CreatedByUserId,
            a.IdempotencyKey,
            a.Status,
            a.Strategy,
            a.SourceKind,
            a.SourceFileName,
            a.TotalRows,
            a.ProcessedRows,
            a.SuccessCount,
            a.UpdatedCount,
            a.SkippedCount,
            a.FailedCount,
            a.CreatedAtUtc,
            a.StartedAtUtc,
            a.CompletedAtUtc,
            a.CanceledAtUtc,
            a.CanceledByUserId,
            a.FailureReason
        );
}
