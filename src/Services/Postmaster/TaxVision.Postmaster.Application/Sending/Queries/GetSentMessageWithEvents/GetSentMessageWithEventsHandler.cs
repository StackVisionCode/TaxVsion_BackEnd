using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageWithEvents;

public static class GetSentMessageWithEventsHandler
{
    public static async Task<Result<SentMessageWithEventsDto>> Handle(
        GetSentMessageWithEventsQuery query,
        ISentMessageRepository repository,
        CancellationToken ct
    )
    {
        var lookup = await repository.GetByIdWithEventsAsync(query.TenantId, query.SentMessageId, ct);
        if (lookup.IsFailure)
            return Result.Failure<SentMessageWithEventsDto>(lookup.Error);

        var message = lookup.Value;
        var timeline = message
            .Events.OrderBy(e => e.EventAtUtc)
            .Select(e => new SentMessageEventDto(e.Id, e.EventType.ToString(), e.EventAtUtc, e.RecipientId, e.Reason))
            .ToList();

        return Result.Success(
            new SentMessageWithEventsDto(
                message.Id,
                message.Status.ToString(),
                message.QueuedAtUtc,
                message.SentAtUtc,
                timeline
            )
        );
    }
}
