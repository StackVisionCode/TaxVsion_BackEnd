using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Audit.Queries;

public static class GetAuditLogsHandler
{
    public static async Task<Result<PagedResult<AuditLogEntryResponse>>> Handle(
        GetAuditLogsQuery query,
        ISubscriptionAuditLogRepository auditLogs,
        CancellationToken ct
    )
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var (entries, totalCount) = await auditLogs.SearchAsync(
            query.TenantId,
            query.AggregateType,
            query.AggregateId,
            query.FromUtc,
            query.ToUtc,
            page,
            pageSize,
            ct
        );

        var items = new List<AuditLogEntryResponse>(entries.Count);
        foreach (var entry in entries)
        {
            items.Add(
                new AuditLogEntryResponse(
                    entry.Id,
                    entry.AggregateType,
                    entry.AggregateId,
                    entry.Action,
                    entry.ActorUserId,
                    entry.ActorType,
                    entry.OccurredAtUtc,
                    entry.CorrelationId,
                    entry.BeforePayload,
                    entry.AfterPayload,
                    entry.Reason
                )
            );
        }

        return Result.Success(new PagedResult<AuditLogEntryResponse>(items, page, pageSize, totalCount));
    }
}
