using BuildingBlocks.Common;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Queries.ListSentMessages;

public static class ListSentMessagesHandler
{
    private const int DefaultSize = 25;
    private const int MaxSize = 100;

    public static async Task<PagedResult<SentMessageSummaryDto>> Handle(
        ListSentMessagesQuery query,
        ISentMessageRepository repository,
        CancellationToken ct
    )
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var size = query.Size is <= 0 or > MaxSize ? DefaultSize : query.Size;

        var (messages, total) = await repository.ListForActivityAsync(query.TenantId, query.Filter, page, size, ct);
        var items = messages.Select(m => ToSummary(m, query.MaskRecipients)).ToList();
        return new PagedResult<SentMessageSummaryDto>(items, page, size, total);
    }

    private static SentMessageSummaryDto ToSummary(SentMessage message, bool mask) =>
        new(
            message.Id,
            message.Subject,
            message.Recipients.Select(r => mask ? EmailAddressMask.Mask(r.Address) : r.Address).ToList(),
            message.DeliveryLane.ToString(),
            SentMessageActivityStatus.Of(message),
            message.QueuedAtUtc,
            message.SentAtUtc,
            message.LastEventAtUtc,
            message.Recipients.Any(r => r.OpenCount > 0),
            message.Recipients.Any(r => r.ClickCount > 0)
        );
}
