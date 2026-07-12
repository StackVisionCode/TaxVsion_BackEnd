using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Audit.Queries;

public sealed record AuditLogResponse(
    Guid Id,
    Guid? UserId,
    string Action,
    string? TargetType,
    Guid? TargetId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    string? DetailsJson,
    bool Success,
    DateTime OccurredAtUtc
);

public sealed record GetAuditLogsQuery(
    Guid TenantId,
    Guid? UserId = null,
    string? Action = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int Size = 50
);

public static class GetAuditLogsHandler
{
    public static async Task<Result<PagedResult<AuditLogResponse>>> Handle(
        GetAuditLogsQuery query,
        IAuthAuditReader reader,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 200)
        {
            return Result.Failure<PagedResult<AuditLogResponse>>(
                new Error("Query.Pagination", "Page must be >= 1 and size between 1 and 200.")
            );
        }

        var (items, total) = await reader.GetPagedAsync(
            query.TenantId,
            query.UserId,
            query.Action,
            query.FromUtc,
            query.ToUtc,
            query.Page,
            query.Size,
            ct
        );

        IReadOnlyList<AuditLogResponse> responses = items
            .Select(log => new AuditLogResponse(
                log.Id,
                log.UserId,
                log.Action,
                log.TargetType,
                log.TargetId,
                log.IpAddress,
                log.UserAgent,
                log.CorrelationId,
                log.DetailsJson,
                log.Success,
                log.OccurredAtUtc
            ))
            .ToList();

        return Result.Success(new PagedResult<AuditLogResponse>(responses, query.Page, query.Size, total));
    }
}
