using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Notifications.Queries;

public sealed record NotificationResponse(
    Guid Id,
    string Channel,
    string Recipient,
    string Subject,
    string TemplateKey,
    string Status,
    string? Error,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc
);

public sealed record GetNotificationsQuery(
    Guid TenantId,
    NotificationStatus? Status = null,
    int Page = 1,
    int Size = 20
);

public static class GetNotificationsHandler
{
    public static async Task<Result<PagedResult<NotificationResponse>>> Handle(
        GetNotificationsQuery query,
        INotificationLogRepository logs,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100)
        {
            return Result.Failure<PagedResult<NotificationResponse>>(
                new Error("Query.Pagination", "Page must be >= 1 and size between 1 and 100.")
            );
        }

        var (items, total) = await logs.GetPagedAsync(query.TenantId, query.Status, query.Page, query.Size, ct);

        IReadOnlyList<NotificationResponse> responses = items
            .Select(log => new NotificationResponse(
                log.Id,
                log.Channel.ToString(),
                log.Recipient,
                log.Subject,
                log.TemplateKey,
                log.Status.ToString(),
                log.Error,
                log.CreatedAtUtc,
                log.SentAtUtc
            ))
            .ToList();

        return Result.Success(new PagedResult<NotificationResponse>(responses, query.Page, query.Size, total));
    }
}
