using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageDetail;

public static class GetSentMessageDetailHandler
{
    public static async Task<Result<SentMessageDetailDto>> Handle(
        GetSentMessageDetailQuery query,
        ISentMessageRepository repository,
        CancellationToken ct
    )
    {
        var lookup = await repository.GetDetailAsync(query.TenantId, query.SentMessageId, ct);
        if (lookup.IsFailure)
            return Result.Failure<SentMessageDetailDto>(lookup.Error);

        var message = lookup.Value;
        string Show(string address) => query.MaskRecipients ? EmailAddressMask.Mask(address) : address;

        var addressById = message.Recipients.ToDictionary(r => r.Id, r => r.Address);
        var timeline = message
            .Events.OrderBy(e => e.EventAtUtc)
            .Select(e => new SentMessageTimelineEntryDto(
                e.EventType.ToString(),
                e.EventAtUtc,
                e.RecipientId is { } id && addressById.TryGetValue(id, out var address) ? Show(address) : null,
                e.Reason
            ))
            .ToList();

        return Result.Success(
            new SentMessageDetailDto(
                message.Id,
                message.Subject,
                message.FromAddress,
                message.DeliveryLane.ToString(),
                SentMessageActivityStatus.Of(message),
                message.QueuedAtUtc,
                message.SentAtUtc,
                message.ErrorReason,
                message.Recipients.Select(r => ToDto(r, Show(r.Address))).ToList(),
                timeline
            )
        );
    }

    private static SentMessageRecipientDetailDto ToDto(SentMessageRecipient recipient, string address) =>
        new(
            recipient.Id,
            address,
            recipient.Type.ToString(),
            recipient.Outcome?.ToString(),
            recipient.OutcomeAtUtc,
            recipient.ErrorReason,
            recipient.OpenCount,
            recipient.FirstOpenedAtUtc,
            recipient.ClickCount,
            recipient.FirstClickedAtUtc,
            recipient.UnsubscribedAtUtc
        );
}
